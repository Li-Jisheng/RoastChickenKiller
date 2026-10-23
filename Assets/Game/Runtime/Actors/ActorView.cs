using System;
using UnityEngine;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Runtime.View;

namespace CK.Runtime.Actors
{
    /// <summary>
    /// 运行时圆柱角色：身体表现、身体碰撞和程序化剑。
    /// 尺寸完全来自配置命中体积，绝不来自常量。
    /// </summary>
    public sealed class ActorView : MonoBehaviour
    {
        public int ActorId { get; private set; }
        public Faction Faction { get; private set; }
        public ActorConfig Config { get; private set; }
        public HitVolumeConfig BodyVolume { get; private set; }
        public CharacterController Controller { get; private set; }
        public CapsuleCollider Capsule { get; private set; }
        public ProceduralSwordView Sword { get; private set; }
        public CollisionFlags LastCollisionFlags { get; private set; }
        public Vector3 LastMoveDelta { get; private set; }
        public string DisplayName => Config != null ? Config.DisplayName : name;

        private MeshRenderer _bodyRenderer;
        private Material _bodyMaterial;
        private Color _bodyColor = Color.white;
        private float _flashUntil;
        private float _verticalSpeed;

        public bool IsPlayer => Faction == Faction.Player;

        public static ActorView Create(int actorId, ActorConfig config, HitVolumeConfig bodyVolume,
            Vector3 spawn, float yaw, WeaponConfig weapon)
        {
            var root = new GameObject($"Actor_{actorId}_{config.DisplayName}");
            root.transform.position = spawn;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var view = root.AddComponent<ActorView>();
            view.ActorId = actorId;
            view.Faction = config.Faction;
            view.Config = config;
            view.BodyVolume = bodyVolume;
            view.BuildBody(config, bodyVolume);
            view.Sword = ProceduralSwordView.Create(root.transform, config.Faction, weapon, view._bodyMaterial);
            view.ApplySwordPose(null, null, false);
            return view;
        }

        private void BuildBody(ActorConfig config, HitVolumeConfig volume)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "BodyCylinder";
            Collider primitiveCollider = body.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
                Destroy(primitiveCollider);
            }
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, volume.CenterY, 0f);
            body.transform.localScale = new Vector3(volume.Radius * 2f, volume.Height * 0.5f, volume.Radius * 2f);
            _bodyRenderer = body.GetComponent<MeshRenderer>();
            _bodyMaterial = CreateMaterial(ProceduralSwordView.ParseColor(config.Color));
            if (_bodyRenderer != null)
            {
                _bodyRenderer.sharedMaterial = _bodyMaterial;
            }
            _bodyColor = ProceduralSwordView.ParseColor(config.Color);

            if (config.Faction == Faction.Player)
            {
                Controller = gameObject.AddComponent<CharacterController>();
                Controller.radius = volume.Radius;
                Controller.height = volume.Height;
                Controller.center = new Vector3(0f, volume.CenterY, 0f);
                Controller.slopeLimit = 45f;
                Controller.stepOffset = volume.Radius * 0.5f;
                Controller.skinWidth = 0.08f;
                Controller.minMoveDistance = 0f;
            }
            else
            {
                Capsule = gameObject.AddComponent<CapsuleCollider>();
                Capsule.radius = volume.Radius;
                Capsule.height = volume.Height;
                Capsule.center = new Vector3(0f, volume.CenterY, 0f);
                Capsule.direction = 1;
            }
        }

        public void Move(Vector3 delta)
        {
            if (Controller == null)
            {
                return;
            }
            LastMoveDelta = delta;
            LastCollisionFlags = Controller.Move(delta);
        }

        public void SetWeapon(WeaponConfig weapon)
        {
            if (Sword != null)
            {
                Sword.Configure(weapon);
            }
        }

        public void ApplySwordPose(AbilityConfig ability, ActorSnapshot snapshot, bool blocking)
        {
            if (Sword == null)
            {
                return;
            }
            int frame = snapshot != null ? snapshot.AbilityFrame : 0;
            bool useAbility = snapshot != null && snapshot.Busy && ability != null;
            Sword.ApplyAbilityPose(useAbility ? ability : null, snapshot != null ? snapshot.Phase : -1, frame, blocking);
        }

        public CapsuleVolume GetBodyCapsule()
        {
            float halfSegment = Mathf.Max(0f, BodyVolume.Height * 0.5f - BodyVolume.Radius);
            Vector3 center = transform.position + Vector3.up * BodyVolume.CenterY;
            Vector3 up = transform.up * halfSegment;
            Vector3 a = center - up;
            Vector3 b = center + up;
            return new CapsuleVolume
            {
                Ax = a.x, Ay = a.y, Az = a.z,
                Bx = b.x, By = b.y, Bz = b.z,
                Radius = BodyVolume.Radius
            };
        }

        public CapsuleVolume GetWeaponCapsule(out bool active)
        {
            active = false;
            if (Sword == null || !Sword.TryGetWorldSegment(out Vector3 start, out Vector3 end, out float radius))
            {
                return default;
            }
            active = true;
            return new CapsuleVolume
            {
                Ax = start.x, Ay = start.y, Az = start.z,
                Bx = end.x, By = end.y, Bz = end.z,
                Radius = radius
            };
        }

        public void Flash(Color color, float seconds)
        {
            _flashUntil = Time.time + seconds;
            if (_bodyMaterial != null)
            {
                SetMaterialColor(Color.Lerp(_bodyColor, color, 0.75f));
            }
        }

        public void TickVisual()
        {
            if (_bodyMaterial != null && Time.time >= _flashUntil)
            {
                SetMaterialColor(_bodyColor);
            }
            if (Sword != null)
            {
                Sword.TickVisual();
            }
        }

        /// <summary>立即面向世界方向；移动方向相对相机。</summary>
        public void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        public void Teleport(Vector3 position)
        {
            bool hadController = Controller != null;
            if (hadController)
            {
                Controller.enabled = false;
            }
            transform.position = position;
            _verticalSpeed = 0f;
            if (hadController)
            {
                Controller.enabled = true;
            }
        }

        public void AddVerticalSpeed(float value)
        {
            _verticalSpeed += value;
        }

        public float VerticalSpeed => _verticalSpeed;
        public void SetVerticalSpeed(float value)
        {
            _verticalSpeed = value;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                Debug.LogError("[CK] actor shader missing (URP/Lit not included in this build)");
                return null;
            }
            var material = new Material(shader) { name = "ActorRuntimeMaterial" };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.15f);
            }
            return material;
        }

        private void SetMaterialColor(Color color)
        {
            if (_bodyMaterial == null)
            {
                return;
            }
            if (_bodyMaterial.HasProperty("_BaseColor"))
            {
                _bodyMaterial.SetColor("_BaseColor", color);
            }
            else if (_bodyMaterial.HasProperty("_Color"))
            {
                _bodyMaterial.SetColor("_Color", color);
            }
            else
            {
                _bodyMaterial.color = color;
            }
        }
    }
}
