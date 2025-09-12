using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UIElements;

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    [RequireComponent(typeof(UIDocument))]
    public class FirstPersonController : MonoBehaviour
    {



        // -------- HUD / UI --------
        [Header("HUD / UI")]
        public PlayerStats player;               // Assign in Inspector (falls back to GetComponent)
        private UIDocument _uiDoc;
        private ProgressBar _healthBar;
        private Label _ammoLabel;
        private Label _weaponLabel;

        // -------- Player movement --------
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 4.0f;
        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 6.0f;
        [Tooltip("Rotation speed of the character")]
        public float RotationSpeed = 1.0f;
        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        [Tooltip("Acceleration time to go from 50% to 100% speed")]
        public float AccelerationTime = 2f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;
        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.1f;
        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;
        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;
        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.5f;
        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;
        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 90.0f;
        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -90.0f;

        // cinemachine
        private float _cinemachineTargetPitch;

        // player
        private float _currentSpeed;
        private float _targetSpeed;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // acceleration timer
        private float _accelTimer;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }

         // ---------------- Unity lifecycle ----------------


        private void Awake()
        {
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError("Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

                        if (!player) player = GetComponent<PlayerStats>();

            // UI hookup (do this in Start so UIDocument is ready)
            _uiDoc = GetComponent<UIDocument>();
            if (_uiDoc != null)
            {
                var root = _uiDoc.rootVisualElement;
                _healthBar = root.Q<ProgressBar>("HealthBar");
                _ammoLabel = root.Q<Label>("AmmoLabel");
                _weaponLabel = root.Q<Label>("WeaponLabel");
            }

            // Subscribe + initialize HUD
            if (player != null)
            {
                player.OnHealthChanged += UpdateHealth;
                player.OnAmmoChanged += UpdateAmmo;
                player.OnWeaponChanged += UpdateWeapon;

                UpdateHealth(player.Health, player.maxHealth);
                UpdateAmmo(player.AmmoInMag, player.AmmoReserve);
                UpdateWeapon(player.WeaponName);
            }
            else
            {
                Debug.LogWarning("FirstPersonController: No PlayerStats found/assigned.");
            }
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.OnHealthChanged -= UpdateHealth;
                player.OnAmmoChanged -= UpdateAmmo;
                player.OnWeaponChanged -= UpdateWeapon;
            }
        }

        private void Update()
        {
            JumpAndGravity();
            GroundedCheck();
            Move();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        // ---------------- HUD callbacks ----------------

        private void UpdateHealth(int current, int max)
        {
            if (_healthBar == null) return;
            _healthBar.highValue = max;
            _healthBar.value = current;
            _healthBar.title = $"Health {current}/{max}";
        }

        private void UpdateAmmo(int mag, int reserve)
        {
            if (_ammoLabel == null) return;
            _ammoLabel.text = $"{mag} / {reserve}";
        }

        private void UpdateWeapon(string name)
        {
            if (_weaponLabel == null) return;
            _weaponLabel.text = name;
        }

        // ---------------- Movement / camera ----------------


        private void GroundedCheck()
        {
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
        }

        private void CameraRotation()
        {
            // if there is an input
            if (_input.look.sqrMagnitude >= _threshold)
            {
                //Don't multiply mouse input by Time.deltaTime
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
                _rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

                // clamp our pitch rotation
                _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

                // Update Cinemachine camera target pitch
                CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

                // rotate the player left and right
                transform.Rotate(Vector3.up * _rotationVelocity);
            }
        }

        private void Move()
        {
            // determine base target speed
            float baseSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            // reset accel if no input
            if (_input.move == Vector2.zero)
            {
                _targetSpeed = 0f;
                _accelTimer = 0f;
            }
            else
            {
                // accelerate from 50% → 100% of baseSpeed
                _accelTimer += Time.deltaTime;
                float accelFactor = Mathf.Clamp01(_accelTimer / AccelerationTime);
                float accelMultiplier = Mathf.Lerp(0.5f, 1f, accelFactor);
                _targetSpeed = baseSpeed * accelMultiplier;
            }

            // current horizontal velocity
            Vector3 horizontalVelocity = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
            float currentHorizontalSpeed = horizontalVelocity.magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // smooth speed towards target
            if (currentHorizontalSpeed < _targetSpeed - speedOffset || currentHorizontalSpeed > _targetSpeed + speedOffset)
            {
                _currentSpeed = Mathf.Lerp(currentHorizontalSpeed, _targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
                _currentSpeed = Mathf.Round(_currentSpeed * 1000f) / 1000f;
            }
            else
            {
                _currentSpeed = _targetSpeed;
            }

            // input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
            if (_input.move != Vector2.zero)
            {
                inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
            }

            // ✅ keep inertia in air
            Vector3 moveVector;
            if (Grounded)
            {
                moveVector = inputDirection.normalized * (_currentSpeed * Time.deltaTime);
            }
            else
            {
                // if no input → keep previous horizontal momentum
                if (_input.move == Vector2.zero)
                    moveVector = horizontalVelocity * Time.deltaTime;
                else
                    moveVector = inputDirection.normalized * (_currentSpeed * Time.deltaTime);
            }

            // apply move and gravity
            _controller.Move(moveVector + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }

                // if we are not grounded, do not jump
                _input.jump = false;
            }

            // apply gravity
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
        }
    }
}
