using UnityEngine;
using CK.Domain.Config;

namespace CK.Runtime.View
{
    /// <summary>
    /// Procedural cylinder sword. The same pose drives both the rendered blade and
    /// the capsule segment submitted for hit detection, so what you see is what hits.
    /// </summary>
    public sealed class ProceduralSwordView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        private Transform _pivot;
        private Transform _blade;
        private MeshRenderer _renderer;
        private Material _material;
        private WeaponConfig _weapon;
        private Color _baseColor = Color.gray;
        private float _flashUntil;
        private Color _flashColor = Color.white;
        private int _swingSign = 1;
        private float _currentYaw;
        private float _currentPitch;

        public bool HasWeapon => _weapon != null;
        public WeaponConfig Weapon => _weapon;
        public float Length => _weapon != null ? _weapon.Length : 0f;
        public float Radius => _weapon != null ? _weapon.Radius : 0f;
        public Transform Pivot => _pivot;

        public static ProceduralSwordView Create(Transform actorRoot, Faction faction, WeaponConfig weapon, Material fallbackMaterial)
        {
            var pivot = new GameObject("WeaponPivot").transform;
            pivot.SetParent(actorRoot, false);
            var view = pivot.gameObject.AddComponent<ProceduralSwordView>();
            view._pivot = pivot;
            view._swingSign = faction == Faction.Enemy ? -1 : 1;

            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            blade.name = "SwordBlade";
            Collider collider = blade.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Object.Destroy(collider);
            }
            blade.transform.SetParent(pivot, false);
            view._blade = blade.transform;
            view._renderer = blade.GetComponent<MeshRenderer>();
            view._material = CreateMaterial();
            if (view._material != null)
            {
                view._renderer.sharedMaterial = view._material;
            }
            view.Configure(weapon, fallbackMaterial);
            return view;
        }

        public void Configure(WeaponConfig weapon, Material fallbackMaterial = null)
        {
            _weapon = weapon;
            if (_pivot == null)
            {
                return;
            }
            if (weapon == null)
            {
                _blade.gameObject.SetActive(false);
                return;
            }
            _blade.gameObject.SetActive(true);
            _pivot.localPosition = new Vector3(weapon.HandOffset.X, weapon.HandOffset.Y, weapon.HandOffset.Z);
            _pivot.localRotation = Quaternion.identity;
            _blade.localPosition = new Vector3(0f, 0f, weapon.Length * 0.5f);
            _blade.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _blade.localScale = new Vector3(weapon.Radius * 2f, weapon.Length * 0.5f, weapon.Radius * 2f);

            _baseColor = ParseColor(weapon.Color);
            ApplyColor(_baseColor);
            _currentYaw = 0f;
            _currentPitch = 0f;
            ApplyRotation(0f, 0f);
        }

        /// <summary>Pose derived from the native ability state; deterministic per frame.</summary>
        public void ApplyAbilityPose(AbilityConfig ability, int phase, int frame, bool blocking)
        {
            float yaw = 0f;
            float pitch = 0f;
            if (blocking)
            {
                yaw = 82f * _swingSign;
                pitch = -18f;
            }
            else if (ability != null)
            {
                int total = Mathf.Max(1, ability.EndFrame);
                float t = Mathf.Clamp01(frame / (float)total);
                switch (ability.Slot)
                {
                    case AbilitySlot.Primary:
                    case AbilitySlot.Heavy:
                    case AbilitySlot.Skill1:
                    case AbilitySlot.Skill2:
                    case AbilitySlot.Skill3:
                    case AbilitySlot.Skill4:
                    case AbilitySlot.EnemyAttack:
                        yaw = Mathf.Lerp(-45f, 45f, t) * _swingSign;
                        pitch = Mathf.Lerp(-12f, 18f, Mathf.Sin(t * Mathf.PI));
                        break;
                    case AbilitySlot.Dodge:
                        yaw = -55f * _swingSign;
                        pitch = 20f;
                        break;
                    default:
                        break;
                }
            }
            else
            {
                // Rest pose: angled outward so both swords are readable in screenshots.
                // The enemy is mirrored and opens wider so its blade clears the player body.
                yaw = _swingSign > 0 ? -60f : -110f;
                pitch = -6f;
            }
            _currentYaw = yaw;
            _currentPitch = pitch;
            ApplyRotation(yaw, pitch);
        }

        private void ApplyRotation(float yaw, float pitch)
        {
            if (_pivot != null)
            {
                _pivot.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        public void Flash(Color color, float seconds)
        {
            _flashColor = color;
            _flashUntil = Time.time + seconds;
        }

        public void TickVisual()
        {
            if (_material == null)
            {
                return;
            }
            if (Time.time < _flashUntil)
            {
                ApplyColor(Color.Lerp(_baseColor, _flashColor, 0.8f));
            }
            else if (_material.color != _baseColor)
            {
                ApplyColor(_baseColor);
            }
        }

        /// <summary>World-space capsule segment matching the rendered blade.</summary>
        public bool TryGetWorldSegment(out Vector3 start, out Vector3 end, out float radius)
        {
            start = Vector3.zero;
            end = Vector3.zero;
            radius = 0f;
            if (_weapon == null || _pivot == null || !_blade.gameObject.activeInHierarchy)
            {
                return false;
            }
            start = _pivot.position;
            end = _pivot.position + _pivot.rotation * Vector3.forward * _weapon.Length;
            radius = _weapon.Radius;
            return true;
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                return null;
            }
            var material = new Material(shader) { name = "SwordRuntimeMaterial" };
            if (material.HasProperty(SmoothnessId))
            {
                material.SetFloat(SmoothnessId, 0.25f);
            }
            if (material.HasProperty(MetallicId))
            {
                material.SetFloat(MetallicId, 0.1f);
            }
            return material;
        }

        private void ApplyColor(Color color)
        {
            if (_material == null)
            {
                return;
            }
            if (_material.HasProperty(BaseColorId))
            {
                _material.SetColor(BaseColorId, color);
            }
            else if (_material.HasProperty(ColorId))
            {
                _material.SetColor(ColorId, color);
            }
            else
            {
                _material.color = color;
            }
        }

        public static Color ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color))
            {
                return color;
            }
            return Color.gray;
        }
    }
}
