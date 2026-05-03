using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 1. Added the new Input System namespace!

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public Camera playerCamera;
    public float walkSpeed = 6f;
    public float runSpeed = 12f;
    public float jumpPower = 7f;
    public float gravity = 10f;
    public float lookSpeed = 2f;
    public float lookXLimit = 45f;
    public float defaultHeight = 2f;
    public float crouchHeight = 1f;
    public float crouchSpeed = 3f;

    [Header("Costes de energia")]
    [Tooltip("Energia que cuesta saltar. Sin esta cantidad no puedes saltar.")]
    public float jumpEnergyCost = 15f;
    [Tooltip("Energia por segundo que consume correr (sprint). 0 = gratis.")]
    public float sprintEnergyPerSecond = 8f;

    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;
    private CharacterController characterController;

    private bool canMove = true;

    // 2. We declare our new Input Actions here
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;
    private InputAction crouchAction;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 3. Setup what buttons trigger which actions
        
        // WASD keys for movement
        moveAction = new InputAction("Move");
        moveAction.AddCompositeBinding("Dpad")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        
        // Mouse movement for looking around
        lookAction = new InputAction("Look", binding: "<Mouse>/delta");
        
        // Spacebar to jump
        jumpAction = new InputAction("Jump", binding: "<Keyboard>/space");

        // Left Shift to run
        sprintAction = new InputAction("Sprint", binding: "<Keyboard>/leftShift");
        
        // R to crouch
        crouchAction = new InputAction("Crouch", binding: "<Keyboard>/r");

        // 4. We must enable the actions before we can use them
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        crouchAction.Enable();
    }

    void OnDestroy()
    {
        // 5. It's good practice to disable actions when the object is destroyed
        moveAction.Disable();
        lookAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
        crouchAction.Disable();
    }

    void Update()
    {
        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        // 6. Read the new inputs!
        bool isRunning = sprintAction.IsPressed();
        Vector2 moveInput = moveAction.ReadValue<Vector2>();

        // Gating: para correr necesitas tener energia
        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        if (isRunning && isMoving && sprintEnergyPerSecond > 0f && EnergyManager.Instance != null)
        {
            if (!EnergyManager.Instance.TrySpend(sprintEnergyPerSecond * Time.deltaTime))
            {
                isRunning = false; // sin energia, vuelves a velocidad de andar
            }
        }

        // Note: moveInput.y is W/S (Vertical) and moveInput.x is A/D (Horizontal)
        float curSpeedX = canMove ? (isRunning ? runSpeed : walkSpeed) * moveInput.y : 0;
        float curSpeedY = canMove ? (isRunning ? runSpeed : walkSpeed) * moveInput.x : 0;
        float movementDirectionY = moveDirection.y;
        moveDirection = (forward * curSpeedX) + (right * curSpeedY);

        // Check if jump was pressed exactly on this frame
        if (jumpAction.WasPressedThisFrame() && canMove && characterController.isGrounded)
        {
            // Gating: salto solo si hay energia suficiente
            bool canJump = EnergyManager.Instance == null
                ? true
                : EnergyManager.Instance.TrySpend(jumpEnergyCost);

            if (canJump)
                moveDirection.y = jumpPower;
            else
                moveDirection.y = movementDirectionY;
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime;
        }

        if (crouchAction.IsPressed() && canMove)
        {
            characterController.height = crouchHeight;
            walkSpeed = crouchSpeed;
            runSpeed = crouchSpeed;
        }
        else
        {
            characterController.height = defaultHeight;
            walkSpeed = 6f;
            runSpeed = 12f;
        }

        characterController.Move(moveDirection * Time.deltaTime);

        if (canMove)
        {
            Vector2 lookInput = lookAction.ReadValue<Vector2>();

            // 7. Important Mouse Sensitivity Adjustment
            // The old system smoothed mouse inputs. The new system reads exact raw pixels.
            // We multiply by 0.1f so your camera doesn't spin wildly out of control!
            float mouseSensitivityMultiplier = 0.1f; 

            rotationX += -lookInput.y * lookSpeed * mouseSensitivityMultiplier;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            
            transform.rotation *= Quaternion.Euler(0, lookInput.x * lookSpeed * mouseSensitivityMultiplier, 0);
        }
    }
}