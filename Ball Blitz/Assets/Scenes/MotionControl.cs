using UnityEngine;
using UnityEngine.InputSystem;
using System;

[DisallowMultipleComponent]
public class MotionControl : MonoBehaviour
{
    Animator animator;
    CharacterController controller;

    [Header("Camera / Mouse")]
    public Transform cameraTransform;
    public Transform cameraPivot;
    public float pivotHeight = 1.6f;
    public float mouseSensitivity = 0.2f;
    public float minVerticalAngle = -30f;
    public float maxVerticalAngle = 60f;
    public bool lockCursorOnStart = true;

    [Header("Animation State Names (must match your Animator)")]
    public string animIdle = "Idle";
    public string animSprint = "Sprint";
    public string animJogForward = "Jog Forward";
    public string animJogForwardDiagonal = "Jog Forward Diagonal Left";
    public string animJogForwardDiagonalLeft = "Jog Forward Diagonal";
    public string animJogBackward = "Jog Backward";
    public string animJogBackwardDiagonal = "Jog Backward Diagonal";
    public string animJogBackwardDiagonalLeft = "Jog Backward Diagonal Left";
    public string animStrafe = "Strafe";
    public string animStrafeLeft = "Strafe Left";
    public string animJump = "Jump";
    public string animRunningRightTurn = "Running Right Turn";
    public string animRunningLeftTurn = "Running Left Turn";

    [Header("Movement")]
    public float walkSpeed = 3.5f;
    public float sprintMultiplier = 1.8f;
    public float jumpForce = 5f;
    public float gravity = -20f;

    [Header("Animation tuning")]
    public float minAnimSwitchInterval = 0.08f;
    public float transitionGrace = 0.06f;
    public float crossFadeDuration = 0.12f;
    public float smallLoopFade = 0.05f;

    [Header("Sprint rules")]
    public float sprintForwardThreshold = 0.1f;
    public float sprintDiagonalThreshold = 0.35f;

    float currentPitch = -14.312f;
    bool cursorLocked = false;
    Vector3 cameraInitialLocalPos = Vector3.back * 3f;
    bool haveInitialLocalPos = false;

    Vector3 inputDir = Vector3.zero;
    Vector3 prevInputDir = Vector3.zero;
    bool wantSprint = false;

    float verticalVelocity = 0f;
    Vector3 horizontalVelocity = Vector3.zero;

    bool wasGrounded = false;
    string desiredState = null;
    string lastRequestedState = null;
    float lastAnimRequestTime = -10f;

    string currentMotionState = null;
    bool isJumping = false;

    string pendingStateAfterSinglePlay = null;
    bool landedWaitingForJumpToFinish = false;

    bool isSprintTurning = false;

    bool sprintAndJumpRestricted = false;

    bool jumpRequestedFlag = false;

    void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();

        if (animator == null) Debug.LogError("MotionControl: Animator not found on same GameObject.");
        if (controller == null) Debug.LogError("MotionControl: CharacterController not found on same GameObject.");

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.speed = 1f;
        }

        wasGrounded = (controller != null) ? controller.isGrounded : false;
        currentMotionState = animIdle;
        isJumping = false;

        if (animator != null) RequestAnimImmediate(animIdle);
    }

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform != null && cameraPivot == null)
        {
            if (cameraTransform.parent != null && cameraTransform.parent != transform)
            {
                cameraPivot = cameraTransform.parent;
            }
            else
            {
                GameObject pivotGO = new GameObject("CameraPivot");
                pivotGO.transform.SetParent(transform, true);
                pivotGO.transform.localPosition = new Vector3(0f, pivotHeight, 0f);

                cameraTransform.SetParent(pivotGO.transform, true);
                cameraPivot = pivotGO.transform;
            }
        }

        if (cameraPivot != null && cameraPivot.parent == transform)
            cameraPivot.localPosition = new Vector3(0f, pivotHeight, 0f);

        if (cameraTransform != null && cameraPivot != null)
        {
            cameraInitialLocalPos = cameraPivot.InverseTransformPoint(cameraTransform.position);
            haveInitialLocalPos = true;
        }

        if (cameraPivot != null)
        {
            float rawX = cameraPivot.localEulerAngles.x;
            float initialPitch = (rawX > 180f) ? rawX - 360f : rawX;
            currentPitch = initialPitch;
        }
        else
        {
            currentPitch = -14.312f;
        }

        if (lockCursorOnStart) SetCursorLocked(true);
    }

    public string GetCurrentMotionState() => currentMotionState;

    public void RestrictSprintsAndJumpCalls() => sprintAndJumpRestricted = true;
    public void EnableSprintsAndJumpCalls() => sprintAndJumpRestricted = false;

    public void SetJumpRequested()
    {
        if (!sprintAndJumpRestricted) jumpRequestedFlag = true;
    }

    public void OnUpdate()
    {
        if (animator == null || controller == null) return;

        var kb_for_camera = Keyboard.current;
        var mouse = Mouse.current;

        if (kb_for_camera != null)
        {
            if (kb_for_camera.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            if (kb_for_camera.lKey.wasPressedThisFrame) SetCursorLocked(true);
        }

        Vector2 mouseDelta = Vector2.zero;
        if (mouse != null) mouseDelta = mouse.delta.ReadValue();

        if (cursorLocked)
        {
            float yaw = mouseDelta.x * mouseSensitivity * Time.deltaTime;
            transform.Rotate(0f, yaw, 0f, Space.World);

            if (cameraPivot != null)
            {
                float pitchDelta = -mouseDelta.y * mouseSensitivity * Time.deltaTime;
                currentPitch = Mathf.Clamp(currentPitch + pitchDelta, minVerticalAngle, maxVerticalAngle);

                Vector3 lp = cameraPivot.localEulerAngles;
                cameraPivot.localEulerAngles = new Vector3(currentPitch, lp.y, lp.z);
            }
        }

        if (cameraPivot != null && cameraPivot.parent == transform)
            cameraPivot.localPosition = new Vector3(0f, pivotHeight, 0f);

        if (haveInitialLocalPos && cameraTransform != null && cameraPivot != null)
            cameraTransform.localPosition = cameraInitialLocalPos;

        float h = 0f, v = 0f;
        var kb = Keyboard.current;
        bool jumpPressedThisFrame = false;

        if (kb != null)
        {
            if (kb.aKey.isPressed) h -= 1f;
            if (kb.dKey.isPressed) h += 1f;
            if (kb.wKey.isPressed) v += 1f;
            if (kb.sKey.isPressed) v -= 1f;
            wantSprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.spaceKey.wasPressedThisFrame) jumpPressedThisFrame = true;
        }
        else
        {
            try
            {
                h = Input.GetAxisRaw("Horizontal");
                v = Input.GetAxisRaw("Vertical");
                wantSprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (Input.GetKeyDown(KeyCode.Space)) jumpPressedThisFrame = true;
            }
            catch { }
        }

        var gp = Gamepad.current;
        if (gp != null)
        {
            Vector2 stick = gp.leftStick.ReadValue();
            h += stick.x;
            v += stick.y;
            if (gp.leftTrigger.ReadValue() > 0.5f) wantSprint = true;
            if (gp.buttonSouth.wasPressedThisFrame) jumpPressedThisFrame = true;
        }

        Vector2 raw = new Vector2(h, v);
        if (raw.sqrMagnitude > 1f) raw = raw.normalized;

        inputDir = new Vector3(raw.x, 0f, raw.y);

        if (jumpPressedThisFrame)
        {
            if (!sprintAndJumpRestricted) jumpRequestedFlag = true;
        }

        ComputeDesiredState();

        if (!isJumping && !isSprintTurning)
        {
            TryApplyDesiredState();
        }

        MaintainPlayback();
    }

    public void OnFixedUpdate()
    {
        if (animator == null || controller == null) return;

        prevInputDir = inputDir;

        float targetSpeed = walkSpeed * (wantSprint ? sprintMultiplier : 1f);
        Vector3 worldMove = (Mathf.Abs(inputDir.x) + Mathf.Abs(inputDir.z) > 0.0001f)
            ? (controller.transform.right * inputDir.x + controller.transform.forward * inputDir.z) * targetSpeed
            : Vector3.zero;
        horizontalVelocity = worldMove;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;

            if (jumpRequestedFlag)
            {
                if (!sprintAndJumpRestricted)
                {
                    verticalVelocity = jumpForce;

                    isJumping = true;
                    pendingStateAfterSinglePlay = null;
                    landedWaitingForJumpToFinish = false;
                    RequestAnimImmediate(animJump);
                    currentMotionState = animJump;
                }

                jumpRequestedFlag = false;
            }
        }
        else
        {
            verticalVelocity += gravity * Time.fixedDeltaTime;
            if (!isJumping) isJumping = true;
        }

        Vector3 move = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(move * Time.fixedDeltaTime);

        bool groundedNow = controller.isGrounded;
        if (groundedNow && !wasGrounded)
        {
            ComputeDesiredState();
            string stateToApply = desiredState ?? animIdle;

            if (animator != null && currentMotionState == animJump)
            {
                AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
                bool jumpClipStillPlaying = (cur.shortNameHash == Animator.StringToHash(animJump) && cur.normalizedTime < 1f);

                if (jumpClipStillPlaying)
                {
                    if (stateToApply == animIdle)
                    {
                        pendingStateAfterSinglePlay = stateToApply;
                        landedWaitingForJumpToFinish = true;
                    }
                    else
                    {
                        isJumping = false;
                        pendingStateAfterSinglePlay = null;
                        landedWaitingForJumpToFinish = false;

                        RequestAnim(stateToApply);
                        currentMotionState = stateToApply;
                    }
                }
                else
                {
                    isJumping = false;
                    pendingStateAfterSinglePlay = null;
                    landedWaitingForJumpToFinish = false;

                    if (stateToApply == animIdle)
                    {
                        RequestAnimImmediate(animIdle);
                        currentMotionState = animIdle;
                    }
                    else
                    {
                        RequestAnim(stateToApply);
                        currentMotionState = stateToApply;
                    }
                }
            }
            else
            {
                isJumping = false;
                pendingStateAfterSinglePlay = null;
                landedWaitingForJumpToFinish = false;

                if (stateToApply == animIdle)
                {
                    RequestAnimImmediate(animIdle);
                    currentMotionState = animIdle;
                }
                else
                {
                    RequestAnim(stateToApply);
                    currentMotionState = stateToApply;
                }
            }
        }

        wasGrounded = groundedNow;
    }

    void ComputeDesiredState()
    {
        bool isMoving = inputDir.sqrMagnitude > 0.001f;
        bool isSprinting = wantSprint && isMoving && inputDir.z > sprintForwardThreshold;

        if (isSprinting)
        {
            float h = inputDir.x;
            if (Mathf.Abs(h) > sprintDiagonalThreshold)
            {
                desiredState = (h > 0f) ? animRunningRightTurn : animRunningLeftTurn;
            }
            else
            {
                desiredState = animSprint;
            }
        }
        else
        {
            if (isMoving)
            {
                float v = inputDir.z;
                float h = inputDir.x;

                if (v > 0.1f)
                {
                    if (Mathf.Abs(h) > 0.35f)
                        desiredState = (h > 0f) ? animJogForwardDiagonal : animJogForwardDiagonalLeft;
                    else
                        desiredState = animJogForward;
                }
                else if (v < -0.1f)
                {
                    if (Mathf.Abs(h) > 0.35f)
                        desiredState = (h > 0f) ? animJogBackwardDiagonal : animJogBackwardDiagonalLeft;
                    else
                        desiredState = animJogBackward;
                }
                else
                {
                    if (Mathf.Abs(h) > 0.01f)
                        desiredState = (h > 0f) ? animStrafe : animStrafeLeft;
                    else
                        desiredState = animIdle;
                }
            }
            else
            {
                desiredState = animIdle;
            }
        }
    }

    void TryApplyDesiredState()
    {
        if (animator == null || string.IsNullOrEmpty(desiredState)) return;

        if (!isJumping && desiredState == animIdle && currentMotionState != animIdle)
        {
            RequestAnimImmediate(animIdle);
            currentMotionState = animIdle;
            return;
        }

        if (desiredState != currentMotionState)
        {
            RequestAnim(desiredState);
            return;
        }
    }

    void RequestAnim(string stateName, bool force = false, float fade = -1f)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        if (fade < 0f) fade = crossFadeDuration;

        animator.speed = 1f;

        AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
        int curHash = cur.shortNameHash;
        int targetHash = Animator.StringToHash(stateName);

        if (curHash == targetHash && !animator.IsInTransition(0))
        {
            lastRequestedState = stateName;
            lastAnimRequestTime = Time.time;
            currentMotionState = stateName;
            return;
        }

        if (!force)
        {
            if (Time.time - lastAnimRequestTime < minAnimSwitchInterval) return;
            if (animator.IsInTransition(0) && Time.time - lastAnimRequestTime < transitionGrace) return;
        }

        if (force)
        {
            animator.Play(stateName, 0, 0f);
        }
        else
        {
            animator.CrossFade(stateName, fade, 0);
        }

        lastRequestedState = stateName;
        lastAnimRequestTime = Time.time;
        currentMotionState = stateName;
    }

    void RequestAnimImmediate(string stateName, float fade = 0.08f)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.speed = 1f;
        animator.Play(stateName, 0, 0f);
        lastRequestedState = stateName;
        lastAnimRequestTime = Time.time;
        currentMotionState = stateName;
    }

    bool IsSinglePlayAnimation(string state)
    {
        if (string.IsNullOrEmpty(state)) return false;
        return state == animJump;
    }

    void MaintainPlayback()
    {
        if (animator == null || string.IsNullOrEmpty(currentMotionState)) return;

        AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
        int curHash = cur.shortNameHash;
        int targetHash = Animator.StringToHash(currentMotionState);

        if (curHash != targetHash) return;
        if (animator.IsInTransition(0)) return;

        float normalizedTime = cur.normalizedTime;

        if (IsSinglePlayAnimation(currentMotionState))
        {
            if (normalizedTime >= 1f)
            {
                if (!string.IsNullOrEmpty(pendingStateAfterSinglePlay))
                {
                    string toApply = pendingStateAfterSinglePlay;
                    pendingStateAfterSinglePlay = null;
                    landedWaitingForJumpToFinish = false;
                    isJumping = false;
                    RequestAnim(toApply, true);
                    return;
                }

                animator.speed = 0f;
            }
            else
            {
                if (animator.speed == 0f) animator.speed = 1f;
            }
        }
        else
        {
            if (normalizedTime >= 1f)
            {
                if (animator.speed == 0f) animator.speed = 1f;
                animator.CrossFade(currentMotionState, smallLoopFade, 0, 0f);
            }
            else
            {
                if (animator.speed == 0f) animator.speed = 1f;
            }
        }
    }

    void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}