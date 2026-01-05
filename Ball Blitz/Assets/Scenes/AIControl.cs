using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(DodgerControl))]
public class AIControl : MonoBehaviour
{
    [Header("References")]
    public DodgerControl dodger;          
    public Transform thrower;             

    [Tooltip("Tag used to find active dodgeballs in the scene.")]
    public string ballTag = "Ball";

    [Tooltip("Optional explicit list of teammate dodgers. If left empty, it will auto-find other AIControl components.")]
    public AIControl[] teammates;

    [Header("Court (XZ bounds)")]
    public Vector2 courtMin = new Vector2(-10f, -6f);
    public Vector2 courtMax = new Vector2(10f, 6f);

    [Header("Thrower / distance behaviour")]
    public float safeDistanceFromThrower = 7f;
    public float panicDistanceFromThrower = 4f;
    public float throwerAvoidWeight = 1.0f;

    [Header("Ball avoidance")]
    public float ballAwarenessRadius = 10f;
    public float ballDangerRadius = 6f;
    public float ballAvoidWeight = 2.0f;

    [Header("Teammate separation")]
    public float teammateSeparationRadius = 2.0f;
    public float teammateSeparationWeight = 0.8f;

    [Header("Corner / wall avoidance")]
    public float wallBuffer = 1.5f;
    public float wallAvoidWeight = 0.9f;
    public float cornerPanicDistance = 6f;

    [Header("Movement tuning")]
    public float maxSteerMagnitude = 2.5f;

    [Tooltip("Random jitter strength when under threat.")]
    public float jitterStrength = 0.15f;

    [Tooltip("How quickly movement direction changes. 0 = very smooth, 1 = instant.")]
    [Range(0f, 1f)]
    public float directionSmoothing = 0.25f;

    [Tooltip("If steering is weaker than this and there is no threat, stay idle.")]
    public float idleSteerThreshold = 0.18f;

    [Header("Sprint limits (to keep AI fair)")]
    [Range(0f, 1f)]
    public float sprintChanceWhenScared = 0.7f;
    public float minSprintInterval = 2.0f;
    public float sprintBurstDuration = 0.7f;

    [Header("Rotation / facing")]
    [Tooltip("How fast the dodger turns to look at the thrower.")]
    public float lookAtThrowerSpeed = 8f;

    [Tooltip("If true, the dodger tries to face the thrower most of the time.")]
    public bool faceThrower = true;

    Rigidbody cachedBall;
    float ballSearchTimer;
    const float BALL_SEARCH_INTERVAL = 0.25f;

    Vector3 smoothedLocalMove = Vector3.zero;
    bool sprintingNow = false;
    float sprintEndTime = 0f;
    float lastSprintTime = -999f;

    void Awake()
    {
        if (dodger == null)
            dodger = GetComponent<DodgerControl>();

        if (dodger == null)
            Debug.LogError("AIControl: DodgerControl missing on same GameObject.");

        // Auto-find teammates
        if (teammates == null || teammates.Length == 0)
        {
            List<AIControl> list = new List<AIControl>(FindObjectsOfType<AIControl>());
            list.Remove(this);
            teammates = list.ToArray();
        }

        // Auto-find thrower
        if (thrower == null)
        {
            GameObject t = GameObject.FindGameObjectWithTag("Player");
            if (t != null) thrower = t.transform;
        }
    }

    void Update()
    {
        if (dodger == null)
            return;

        dodger.UpdateLoop();

        UpdateBallCache();
        UpdateBehaviour();
    }

    void FixedUpdate()
    {
        if (dodger == null)
            return;

        dodger.FixedUpdateLoop();
    }

    // MAIN AI LOGIC

    void UpdateBallCache()
    {
        ballSearchTimer -= Time.deltaTime;
        if (ballSearchTimer > 0f) return;
        ballSearchTimer = BALL_SEARCH_INTERVAL;

        cachedBall = null;

        if (string.IsNullOrEmpty(ballTag))
            return;

        GameObject[] balls = GameObject.FindGameObjectsWithTag(ballTag);
        float bestDist = float.MaxValue;
        Vector3 myPos = transform.position;

        foreach (GameObject b in balls)
        {
            Rigidbody rb = b.GetComponent<Rigidbody>();
            if (rb == null) continue;

            float d = Vector3.Distance(myPos, rb.position);
            if (d < bestDist)
            {
                bestDist = d;
                cachedBall = rb;
            }
        }
    }

    void UpdateBehaviour()
    {
        Vector3 worldSteer = Vector3.zero;
        Vector3 myPos = transform.position;

        float distToThrower = Mathf.Infinity;
        Vector3 toThrower = Vector3.zero;

        // Avoid thrower / keep distance
        if (thrower != null)
        {
            toThrower = thrower.position - myPos;
            toThrower.y = 0f;
            distToThrower = toThrower.magnitude;

            if (distToThrower < safeDistanceFromThrower)
            {
                float t = Mathf.Clamp01((safeDistanceFromThrower - distToThrower) / safeDistanceFromThrower);
                Vector3 away = (-toThrower.normalized) * (throwerAvoidWeight * t);
                worldSteer += away;
            }
        }

        // Avoid incoming ball
        bool ballDanger = false;
        if (cachedBall != null)
        {
            Vector3 ballPos = cachedBall.position;
            Vector3 ballVel = cachedBall.linearVelocity;  

            Vector3 toDodger = myPos - ballPos;
            float dist = toDodger.magnitude;

            if (dist < ballAwarenessRadius)
            {
                Vector3 ballDir = ballVel.sqrMagnitude > 0.01f ? ballVel.normalized : Vector3.zero;
                float approachDot = Vector3.Dot(ballDir, toDodger.normalized);

                if (approachDot > 0.5f) 
                {
                    ballDanger = dist < ballDangerRadius;

                    float t = Mathf.Clamp01((ballDangerRadius - dist) / ballDangerRadius);
                    float weight = ballAvoidWeight * (ballDanger ? 1.5f : 0.7f) * t;

                    Vector3 away = toDodger.normalized * weight;
                    worldSteer += away;
                }
            }
        }

        // Separate from teammates
        if (teammates != null)
        {
            foreach (var mate in teammates)
            {
                if (mate == null) continue;
                Vector3 delta = myPos - mate.transform.position;
                delta.y = 0f;
                float dist = delta.magnitude;

                if (dist > 0.001f && dist < teammateSeparationRadius)
                {
                    float t = Mathf.Clamp01((teammateSeparationRadius - dist) / teammateSeparationRadius);
                    Vector3 away = delta.normalized * (teammateSeparationWeight * t);
                    worldSteer += away;
                }
            }
        }

        // Stay away from walls / corners
        Vector2 posXZ = new Vector2(myPos.x, myPos.z);
        Vector2 centerXZ = (courtMin + courtMax) * 0.5f;

        float distLeft = posXZ.x - courtMin.x;
        float distRight = courtMax.x - posXZ.x;
        float distBottom = posXZ.y - courtMin.y;
        float distTop = courtMax.y - posXZ.y;

        bool nearWall = distLeft < wallBuffer || distRight < wallBuffer ||
                        distBottom < wallBuffer || distTop < wallBuffer;

        if (nearWall)
        {
            Vector2 toCenter2D = centerXZ - posXZ;
            Vector3 toCenter = new Vector3(toCenter2D.x, 0f, toCenter2D.y).normalized *
                               wallAvoidWeight;
            worldSteer += toCenter;

            if (thrower != null)
            {
                if (distToThrower < cornerPanicDistance)
                {
                    Vector2 mirrored = new Vector2(
                        posXZ.x < centerXZ.x ? courtMax.x - 1.0f : courtMin.x + 1.0f,
                        posXZ.y < centerXZ.y ? courtMax.y - 1.0f : courtMin.y + 1.0f
                    );
                    Vector2 toOtherSide2D = (mirrored - posXZ).normalized;
                    Vector3 toOtherSide = new Vector3(toOtherSide2D.x, 0f, toOtherSide2D.y);
                    worldSteer += toOtherSide * 0.7f;
                }
            }
        }

        // Decide if we're under threat (for jitter & idle)
        bool underThreat = ballDanger ||
                           (thrower != null && distToThrower < safeDistanceFromThrower + 0.5f) ||
                           nearWall;

        // Add some randomness only when threatened (to avoid constant jitter when safe)
        if (underThreat && jitterStrength > 0f)
        {
            float jx = (Random.value * 2f - 1f) * jitterStrength;
            float jz = (Random.value * 2f - 1f) * jitterStrength;
            worldSteer += new Vector3(jx, 0f, jz);
        }

        // Clamp & apply idle deadzone
        worldSteer.y = 0f;
        float steerMag = worldSteer.magnitude;

        if (!underThreat && steerMag < idleSteerThreshold)
        {
            worldSteer = Vector3.zero;
            steerMag = 0f;
        }
        else if (steerMag > maxSteerMagnitude)
        {
            worldSteer = worldSteer / steerMag * maxSteerMagnitude;
            steerMag = maxSteerMagnitude;
        }

        // Rotate to face the thrower
        if (faceThrower && thrower != null)
        {
            Vector3 faceDir = thrower.position - myPos;
            faceDir.y = 0f;

            if (faceDir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    lookAtThrowerSpeed * Time.deltaTime
                );
            }
        }

        // Convert steering to local movement & smooth
        float lerpT = Mathf.Clamp01(directionSmoothing); 

        if (steerMag > 0.0001f)
        {
            Vector3 worldDir = worldSteer.normalized;
            Vector3 localDir = transform.InverseTransformDirection(worldDir);
            localDir.y = 0f;

            smoothedLocalMove = Vector3.Lerp(smoothedLocalMove, localDir, lerpT);
        }
        else
        {
            smoothedLocalMove = Vector3.Lerp(smoothedLocalMove, Vector3.zero, lerpT);
        }

        bool wantToMove = smoothedLocalMove.sqrMagnitude > 0.02f;

        // Sprint logic
        bool wantSprint = false;
        if (wantToMove)
        {
            bool scaredByThrower = (thrower != null && distToThrower < panicDistanceFromThrower);
            bool scared = scaredByThrower || ballDanger;

            if (scared)
            {
                if (!sprintingNow && Time.time - lastSprintTime > minSprintInterval)
                {
                    if (Random.value < sprintChanceWhenScared)
                    {
                        sprintingNow = true;
                        sprintEndTime = Time.time + sprintBurstDuration;
                        lastSprintTime = Time.time;
                    }
                }
            }
        }

        if (sprintingNow && Time.time >= sprintEndTime)
            sprintingNow = false;

        wantSprint = sprintingNow;

        // Send movement to DodgerControl
        if (wantToMove)
        {
            dodger.SetMovement(smoothedLocalMove, wantSprint);
        }
        else
        {
            dodger.Stop();
        }
    }

    
    public Vector3 GetPosition() => dodger != null ? dodger.GetPosition() : transform.position;
    public string GetMotionState() => dodger != null ? dodger.GetCurrentMotionState() : string.Empty;
}
