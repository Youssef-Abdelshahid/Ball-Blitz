using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class DodgerControl : MonoBehaviour
{
    [Header("Animation State Names")]
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

    public enum MoveDir
    {
        Idle,
        Forward,
        Backward,
        Left,
        Right,
        ForwardLeft,
        ForwardRight,
        BackLeft,
        BackRight,
        Sprint
    }

    Animator animator;
    CharacterController controller;

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

    enum OpType { Move, SetMovement, Jump, Stop, PlayAnim }
    struct PendingOp { public OpType type; public MoveDir dir; public Vector3 vector; public string anim; public bool sprintFlag; }
    readonly Queue<PendingOp> pendingOps = new Queue<PendingOp>();

    public bool externalControl = true;

    [Header("Fallback (keeps grounding safe if external driver stops calling)")]
    public float externalTimeout = 0.25f;
    float lastExternalCallTime = -10f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();

        if (animator == null) Debug.LogWarning("DodgerControl: Animator not found on same GameObject.");
        if (controller == null) Debug.LogError("DodgerControl: CharacterController required.");

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

        lastExternalCallTime = Time.time;
    }

    public void Move(MoveDir state, Vector3 direction)
    {
        PendingOp op = new PendingOp { type = OpType.Move, dir = state, vector = direction, sprintFlag = (state == MoveDir.Sprint) };
        lock (pendingOps) { pendingOps.Enqueue(op); }
    }

    public void SetMovement(Vector3 dir, bool sprint)
    {
        PendingOp op = new PendingOp { type = OpType.SetMovement, vector = dir, sprintFlag = sprint };
        lock (pendingOps) { pendingOps.Enqueue(op); }
    }

    public void Jump()
    {
        PendingOp op = new PendingOp { type = OpType.Jump };
        lock (pendingOps) { pendingOps.Enqueue(op); }
    }

    public void Stop()
    {
        PendingOp op = new PendingOp { type = OpType.Stop };
        lock (pendingOps) { pendingOps.Enqueue(op); }
    }

    public void PlayAnim(string animState)
    {
        PendingOp op = new PendingOp { type = OpType.PlayAnim, anim = animState };
        lock (pendingOps) { pendingOps.Enqueue(op); }
    }


    public void UpdateLoop()
    {
        lastExternalCallTime = Time.time;

        ProcessPendingOps();
        MaintainPlayback();

        if (!isJumping && !isSprintTurning)
        {
            ComputeDesiredState();
            TryApplyDesiredState();
        }
    }


    public void FixedUpdateLoop()
    {
        lastExternalCallTime = Time.time;

        prevInputDir = inputDir;

        float targetSpeed = walkSpeed * (wantSprint ? sprintMultiplier : 1f);

        Vector3 right = (controller != null) ? controller.transform.right : transform.right;
        Vector3 forward = (controller != null) ? controller.transform.forward : transform.forward;

        Vector3 worldMove = (Mathf.Abs(inputDir.x) + Mathf.Abs(inputDir.z) > 0.0001f)
            ? (right * inputDir.x + forward * inputDir.z) * targetSpeed
            : Vector3.zero;
        horizontalVelocity = worldMove;

        if (controller != null && controller.isGrounded)
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
        if (controller != null) controller.Move(move * Time.fixedDeltaTime);

        bool groundedNow = (controller != null) ? controller.isGrounded : true;
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

    void ProcessPendingOps()
    {
        if (pendingOps.Count == 0) return;

        lock (pendingOps)
        {
            while (pendingOps.Count > 0)
            {
                var op = pendingOps.Dequeue();
                switch (op.type)
                {
                    case OpType.Move:
                        ApplyMoveOp(op.dir, op.vector, op.sprintFlag);
                        break;
                    case OpType.SetMovement:
                        inputDir = new Vector3(Mathf.Clamp(op.vector.x, -1f, 1f), 0f, Mathf.Clamp(op.vector.z, -1f, 1f));
                        wantSprint = op.sprintFlag;
                        break;
                    case OpType.Jump:
                        if (!sprintAndJumpRestricted) jumpRequestedFlag = true;
                        break;
                    case OpType.Stop:
                        inputDir = Vector3.zero;
                        wantSprint = false;
                        break;
                    case OpType.PlayAnim:
                        RequestAnim(op.anim);
                        break;
                }
            }
        }
    }

    void ApplyMoveOp(MoveDir state, Vector3 direction, bool sprintFlag)
    {
        switch (state)
        {
            case MoveDir.Forward: inputDir = new Vector3(0f, 0f, 1f); wantSprint = false; break;
            case MoveDir.Backward: inputDir = new Vector3(0f, 0f, -1f); wantSprint = false; break;
            case MoveDir.Left: inputDir = new Vector3(-1f, 0f, 0f); wantSprint = false; break;
            case MoveDir.Right: inputDir = new Vector3(1f, 0f, 0f); wantSprint = false; break;
            case MoveDir.ForwardLeft: inputDir = new Vector3(-0.6f, 0f, 0.8f).normalized; wantSprint = false; break;
            case MoveDir.ForwardRight: inputDir = new Vector3(0.6f, 0f, 0.8f).normalized; wantSprint = false; break;
            case MoveDir.BackLeft: inputDir = new Vector3(-0.6f, 0f, -0.8f).normalized; wantSprint = false; break;
            case MoveDir.BackRight: inputDir = new Vector3(0.6f, 0f, -0.8f).normalized; wantSprint = false; break;
            case MoveDir.Sprint: inputDir = new Vector3(0f, 0f, 1f); wantSprint = true; break;
            case MoveDir.Idle: default: inputDir = Vector3.zero; wantSprint = false; break;
        }

        if (direction.sqrMagnitude > 0.0001f)
        {
            inputDir = new Vector3(direction.x, 0f, direction.z).normalized;
            wantSprint = sprintFlag;
        }
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

    public string GetCurrentMotionState() => currentMotionState;
    public Vector3 GetPosition() => transform.position;
    public void RestrictSprintsAndJumpCalls() => sprintAndJumpRestricted = true;
    public void EnableSprintsAndJumpCalls() => sprintAndJumpRestricted = false;


    void Update()
    {
        if (!externalControl)
        {
            UpdateLoop();
            return;
        }

        if (externalControl && externalTimeout > 0f && Time.time - lastExternalCallTime > externalTimeout)
        {
            UpdateLoop();
        }
    }

    void FixedUpdate()
    {
        if (!externalControl)
        {
            FixedUpdateLoop();
            return;
        }

        if (externalControl && externalTimeout > 0f && Time.time - lastExternalCallTime > externalTimeout)
        {
            FixedUpdateLoop();
        }
    }
}