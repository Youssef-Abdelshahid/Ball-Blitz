using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MotionControl))]
[DisallowMultipleComponent]
public class ThrowerControl : MonoBehaviour
{
    MotionControl mc;
    Animator animator;

    [Header("Pickup settings")]
    public float pickupRange = 1f;
    public LayerMask ballLayer;
    public Transform handTransform;
    public float pickUpDelay = 0.9f;

    [Header("Throw settings")]
    public float throwForce = 10f;
    public float throwDelay = 1.05f;

    [Header("Animation state names (must match Animator)")]
    public string pickUpAnimation = "Taking Item";
    public string throwAnimation = "Goalie Throw";   // or "Throw Object"

    GameObject heldBallObject;
    float originalMass = 1f;

    bool isBusy = false;

    void Awake()
    {
        mc = GetComponent<MotionControl>();
        animator = GetComponent<Animator>();

        if (mc == null)
            Debug.LogError("ThrowerControl: MotionControl not found on same GameObject.");
        if (animator == null)
            Debug.LogError("ThrowerControl: Animator not found on same GameObject.");

        if (handTransform == null)
        {
            var children = GetComponentsInChildren<Transform>();
            foreach (var t in children)
            {
                if (t.name == "BallSocket")
                {
                    handTransform = t;
                    Debug.Log("ThrowerControl: Auto-assigned 'BallSocket'.");
                    break;
                }
            }
        }
    }

    void Update()
    {
        if (mc == null) mc = GetComponent<MotionControl>();

        if (isBusy) return;

        if (mc != null) mc.OnUpdate();

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        bool pickupPressed = kb != null && kb.eKey.wasPressedThisFrame;
        bool throwPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

        if (pickupPressed && heldBallObject == null)
            TryPickUpNearestBall();

        if (throwPressed && heldBallObject != null)
            StartCoroutine(ThrowHeldBall());
    }

    void FixedUpdate()
    {
        if (mc == null) mc = GetComponent<MotionControl>();
        if (mc == null) return;

        if (isBusy) return;

        mc.OnFixedUpdate();
    }

    void TryPickUpNearestBall()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, pickupRange, ballLayer);
        if (hits.Length == 0) return;

        Rigidbody closestRb = null;
        float closestSqr = float.MaxValue;

        foreach (var hit in hits)
        {
            Rigidbody rb = hit.attachedRigidbody ?? hit.GetComponentInParent<Rigidbody>();
            if (rb == null) continue;

            float sqr = (rb.transform.position - transform.position).sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                closestRb = rb;
            }
        }

        if (closestRb != null)
            StartCoroutine(PickUpBall(closestRb));
    }

    IEnumerator PickUpBall(Rigidbody ballRb)
    {
        isBusy = true;
        heldBallObject = ballRb.gameObject;
        originalMass = ballRb.mass;

        mc?.RestrictSprintsAndJumpCalls();

        if (!string.IsNullOrEmpty(pickUpAnimation) && animator != null)
            animator.CrossFade(pickUpAnimation, 0.1f);

        yield return new WaitForSeconds(pickUpDelay);

        if (heldBallObject != null && handTransform != null)
        {
            Destroy(heldBallObject.GetComponent<Rigidbody>());

            var col = heldBallObject.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            heldBallObject.transform.SetParent(handTransform);
            heldBallObject.transform.localPosition = Vector3.zero;
            heldBallObject.transform.localRotation = Quaternion.identity;
        }

        mc?.EnableSprintsAndJumpCalls();
        isBusy = false;
    }

    IEnumerator ThrowHeldBall()
    {
        isBusy = true;

        mc?.RestrictSprintsAndJumpCalls();

        if (!string.IsNullOrEmpty(throwAnimation) && animator != null)
            animator.CrossFade(throwAnimation, 0.1f);

        yield return new WaitForSeconds(throwDelay);

        if (heldBallObject != null)
        {
            heldBallObject.transform.SetParent(null, true);

            var col = heldBallObject.GetComponent<Collider>();
            if (col != null) col.enabled = true;

            Rigidbody rb = heldBallObject.AddComponent<Rigidbody>();
            rb.mass = originalMass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 throwDir = transform.forward.normalized;
            rb.AddForce(throwDir * throwForce, ForceMode.VelocityChange);

            heldBallObject = null;
        }

        mc?.EnableSprintsAndJumpCalls();
        isBusy = false;
    }


    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}