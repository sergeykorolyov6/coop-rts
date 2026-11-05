using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    // Top-down RTS camera: looks at a focus point on the ground from a fixed pitch.
    // WASD, arrows and screen edges pan (along the view), Q and E turn the view around the focus,
    // middle mouse drags the ground, the wheel zooms smoothly, Space returns to the own
    // headquarters. Command hotkeys keep off W, A, S, D, Q and E.
    [RequireComponent(typeof(Camera))]
    public sealed class RtsCamera : MonoBehaviour
    {
        [SerializeField] private SimRunner? _runner;
        [SerializeField] private LocalPlayer? _localPlayer;

        [SerializeField, Range(30f, 89f)] private float _pitch = 55f;
        [SerializeField] private float _minDistance = 8f;
        [SerializeField] private float _maxDistance = 50f;
        [SerializeField] private float _startDistance = 24f;
        [Tooltip("Share of the distance removed per wheel notch.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _zoomStep = 0.12f;
        [Tooltip("How fast the distance follows the wheel: higher is snappier.")]
        [SerializeField, Range(1f, 30f)] private float _zoomSmoothing = 8f;
        [Tooltip("Degrees per second while Q or E is held.")]
        [SerializeField] private float _turnSpeed = 90f;

        [Tooltip("Cells per second at the start distance; scales with zoom.")]
        [SerializeField] private float _panSpeed = 24f;
        [SerializeField] private bool _edgePan = true;
        [SerializeField] private float _edgePixels = 10f;
        [SerializeField] private float _overscrollCells = 2f;

        private Camera? _camera;
        private Vector3 _focus;
        private float _distance;
        private float _targetDistance;
        private float _yaw;
        private Vector2 _dragFrom;
        private bool _dragging;
        private bool _focused;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _distance = _startDistance;
            _targetDistance = _startDistance;
        }

        private void OnEnable()
        {
            if (_localPlayer != null)
                _localPlayer.Changed += FocusHeadquarters;
        }

        private void OnDisable()
        {
            if (_localPlayer != null)
                _localPlayer.Changed -= FocusHeadquarters;
        }

        public void Focus(Vector3 point)
        {
            _focus = new Vector3(point.x, 0f, point.z);
            ClampFocus();
            Apply();
        }

        // Where the screen point hits the ground plane (y = 0); false above the horizon.
        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            point = default;
            if (_camera == null)
                return false;
            Ray ray = _camera.ScreenPointToRay(screen);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter))
                return false;
            point = ray.GetPoint(enter);
            return true;
        }

        // The sim exists only after SimRunner.Awake, so the first focus waits for a frame.
        private void LateUpdate()
        {
            if (!_focused && _runner != null && _runner.Simulation != null)
            {
                _focused = true;
                FocusHeadquarters();
            }

            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            float speed = _panSpeed * (_distance / _startDistance) * Time.unscaledDeltaTime;

            Vector2 pan = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) pan.x -= 1f;
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) pan.x += 1f;
                if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) pan.y -= 1f;
                if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) pan.y += 1f;
                if (keyboard.qKey.isPressed) _yaw += _turnSpeed * Time.unscaledDeltaTime;
                if (keyboard.eKey.isPressed) _yaw -= _turnSpeed * Time.unscaledDeltaTime;
                if (keyboard.spaceKey.wasPressedThisFrame) FocusHeadquarters();
            }

            if (mouse != null)
            {
                Vector2 pointer = mouse.position.ReadValue();
                if (_edgePan && Application.isFocused && InsideScreen(pointer))
                {
                    if (pointer.x <= _edgePixels) pan.x -= 1f;
                    if (pointer.x >= Screen.width - _edgePixels) pan.x += 1f;
                    if (pointer.y <= _edgePixels) pan.y -= 1f;
                    if (pointer.y >= Screen.height - _edgePixels) pan.y += 1f;
                }

                // Grab the ground: the point under the cursor stays under it while dragging.
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    _dragging = true;
                    _dragFrom = pointer;
                }
                if (!mouse.middleButton.isPressed)
                    _dragging = false;
                if (_dragging && GroundPoint(_dragFrom, out Vector3 from) && GroundPoint(pointer, out Vector3 to))
                {
                    _focus += from - to;
                    _dragFrom = pointer;
                }

                // Trackpads send many small deltas, wheels large ones: only the sign counts.
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && InsideScreen(pointer))
                    _targetDistance = Mathf.Clamp(_targetDistance * (1f - Mathf.Sign(scroll) * _zoomStep), _minDistance, _maxDistance);
            }

            // The distance eases towards the wheel's target instead of jumping a notch at a time.
            _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-_zoomSmoothing * Time.unscaledDeltaTime));

            // Pan along the view: "up" is away from the camera whichever way it is turned.
            if (pan != Vector2.zero)
                _focus += Quaternion.Euler(0f, _yaw, 0f) * new Vector3(pan.x, 0f, pan.y).normalized * speed;

            ClampFocus();
            Apply();
        }

        private void FocusHeadquarters()
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null)
                return;
            int playerId = _localPlayer != null ? _localPlayer.PlayerId : 1;
            Building? hq = simulation.State.FindHeadquarters(playerId);
            Focus(hq != null
                ? new Vector3(hq.CellX + hq.Width * 0.5f, 0f, hq.CellY + hq.Height * 0.5f)
                : new Vector3(simulation.State.Grid.Width * 0.5f, 0f, simulation.State.Grid.Height * 0.5f));
        }

        private void ClampFocus()
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null)
                return;
            // Every cell must be reachable, with little void past the map edge. The view is a
            // trapezoid on the ground; turned, any side of it may face any map edge, so the focus keeps
            // the narrowest half-extent of the view (bottom corners, unturned) from every edge.
            Vector3 bottomLeft = GroundOffset(-1f, -1f);
            float inset = Mathf.Min(-bottomLeft.x, -bottomLeft.z);
            _focus.x = ClampAxis(_focus.x, inset - _overscrollCells, simulation.State.Grid.Width - inset + _overscrollCells, simulation.State.Grid.Width);
            _focus.z = ClampAxis(_focus.z, inset - _overscrollCells, simulation.State.Grid.Height - inset + _overscrollCells, simulation.State.Grid.Height);
        }

        // A map smaller than the view stays centred.
        private static float ClampAxis(float value, float min, float max, float size) =>
            min > max ? size * 0.5f : Mathf.Clamp(value, min, max);

        // Where the ray through a viewport point (-1..1 on both axes) meets the ground, relative to
        // the focus. Independent of the focus itself, so it is computed before the camera moves.
        private Vector3 GroundOffset(float x, float y)
        {
            float tanV = Mathf.Tan(_camera!.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Quaternion rotation = Quaternion.Euler(_pitch, 0f, 0f);
            Vector3 eye = -(rotation * Vector3.forward) * _distance;
            Vector3 direction = rotation * new Vector3(x * tanV * _camera.aspect, y * tanV, 1f);
            // A ray above the horizon never lands: treat it as very far.
            if (direction.y >= -0.01f)
                return new Vector3(x * 1000f, 0f, 1000f);
            return eye + direction * (-eye.y / direction.y);
        }

        private void Apply()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_focus - rotation * Vector3.forward * _distance, rotation);
        }

        private static bool InsideScreen(Vector2 p) =>
            p.x >= 0f && p.y >= 0f && p.x <= Screen.width && p.y <= Screen.height;
    }
}
