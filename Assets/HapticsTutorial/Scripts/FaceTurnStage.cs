using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Turn the cube so a highlighted face points at you, for several faces in a row. Works on the
    /// virtual cube's own axes, so it follows the real marked cube when that is tracked and the
    /// hand-grabbed virtual cube otherwise.
    /// </summary>
    public class FaceTurnStage : TutorialStage
    {
        const float FacingDot = 0.85f;
        const float HoldSeconds = 0.6f;
        const float PauseBetweenRounds = 0.8f;

        static readonly Vector3[] FaceOrder =
        {
            Vector3.up, Vector3.right, Vector3.back, Vector3.left, Vector3.forward, Vector3.down
        };

        static readonly Color HighlightColor = new Color(1f, 0.85f, 0.1f, 1f);
        static readonly Color DoneColor = new Color(0.25f, 0.9f, 0.35f, 1f);
        static readonly Color PromptTint = new Color(0.08f, 0.08f, 0.1f, 0.92f);

        Transform _cube;
        InstructionPresenter _presenter;
        int _rounds = 4;
        int _round;
        int _faceCursor;
        Vector3 _face;
        float _held;
        float _nextRoundAt;
        bool _betweenRounds;
        Transform _highlight;
        Material _highlightMat;

        public void Configure(Transform cube, InstructionPresenter presenter, int rounds)
        {
            _cube = cube;
            _presenter = presenter;
            _rounds = Mathf.Max(1, rounds);
        }

        public override void BeginStage()
        {
            base.BeginStage();
            if (_cube == null)
            {
                CompleteStage();
                return;
            }

            _round = 0;
            StartRound();
        }

        public override void EndStage()
        {
            if (_highlight != null)
                _highlight.gameObject.SetActive(false);
            base.EndStage();
        }

        void Update()
        {
            if (!IsActive || _cube == null)
                return;

            if (_betweenRounds)
            {
                if (Time.time < _nextRoundAt)
                    return;
                _betweenRounds = false;
                if (_round >= _rounds)
                {
                    _highlight.gameObject.SetActive(false);
                    CompleteStage();
                    return;
                }

                StartRound();
                return;
            }

            if (FacingAmount(_face) >= FacingDot)
            {
                _held += Time.deltaTime;
                if (_held < HoldSeconds)
                    return;
                _round++;
                SessionLogger.Instance?.Log("face_shown", StageId, $"round={_round} face={_face}");
                SetHighlightColor(DoneColor);
                _betweenRounds = true;
                _nextRoundAt = Time.time + PauseBetweenRounds;
            }
            else
            {
                _held = Mathf.Max(0f, _held - Time.deltaTime);
            }
        }

        void StartRound()
        {
            // Next face in the order that isn't already pointing at the viewer.
            for (var i = 0; i < FaceOrder.Length; i++)
            {
                _face = FaceOrder[_faceCursor++ % FaceOrder.Length];
                if (FacingAmount(_face) < 0.5f)
                    break;
            }

            _held = 0f;
            ShowHighlight(_face);
            _presenter?.Show($"{Title} ({_round + 1}/{_rounds})",
                "Turn the cube so the yellow face points straight at you. Hold it there for a moment.", PromptTint);
        }

        float FacingAmount(Vector3 localFace)
        {
            var eye = Camera.main != null ? Camera.main.transform.position : _cube.position + Vector3.back;
            var normal = _cube.rotation * localFace;
            return Vector3.Dot(normal, (eye - _cube.position).normalized);
        }

        void ShowHighlight(Vector3 localFace)
        {
            if (_highlight == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Face Highlight";
                var collider = quad.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider); // keep the cube's physics and triggers unchanged
                _highlight = quad.transform;
                _highlight.SetParent(_cube, false);

                var cubeRenderer = _cube.GetComponent<Renderer>();
                var renderer = quad.GetComponent<Renderer>();
                if (cubeRenderer != null)
                    renderer.material = new Material(cubeRenderer.sharedMaterial);
                _highlightMat = renderer.material;
            }

            // A quad shows its -Z side; point that outward along the face.
            var up = Mathf.Abs(localFace.y) > 0.5f ? Vector3.forward : Vector3.up;
            _highlight.localPosition = localFace * 0.505f;
            _highlight.localRotation = Quaternion.LookRotation(-localFace, up);
            _highlight.localScale = Vector3.one * 0.8f;
            _highlight.gameObject.SetActive(true);
            SetHighlightColor(HighlightColor);
        }

        void SetHighlightColor(Color color)
        {
            if (_highlightMat == null)
                return;
            if (_highlightMat.HasProperty("_BaseColor"))
                _highlightMat.SetColor("_BaseColor", color);
            _highlightMat.color = color;
        }
    }
}
