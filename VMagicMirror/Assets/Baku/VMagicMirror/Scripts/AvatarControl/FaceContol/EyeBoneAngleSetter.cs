using Baku.VMagicMirror.IK;
using UnityEngine;
using Zenject;

namespace Baku.VMagicMirror
{
    /// <summary>
    /// VMagicMirror実装の中で唯一アバターの目ボーンのrotationを制御する権限のあるクラス(になってほしい…)
    /// </summary>
    /// <remarks>
    /// LateUpdateの中でも非常に遅いタイミングで実行される
    /// NOTE: うまくいくとBlendShape方式の目も動かせるようになるかもしれないが、ストレッチゴール扱い
    /// </remarks>
    public class EyeBoneAngleSetter : MonoBehaviour
    {
        //NOTE: 実験した範囲ではこのリミットを超えることは滅多にない
        private const float RateMagnitudeLimit = 1.2f;
        
        private const float HorizontalRateToAngle = 35f;
        private const float VerticalRateToAngle = 35f;

        //いくら倍率がかかってもコレ以上は無いやろ…という値
        private const float AngleAbsLimit = 80f;

        //Mapが無効なとき、ユーザーが設定したスケールに加えて角度に対して掛ける値。
        //この値をいい感じにすることで、「カーブ設定を使用」がオフのときの挙動に後方互換性っぽいものを持たせる。
        [SerializeField] private float factorWhenMapDisable = 0.2f;

        [SerializeField] private EyeDownMotionController eyeDownMotionController;
        [SerializeField] private EyeJitter eyeJitter;
        [SerializeField] private ExternalTrackerEyeJitter externalTrackerEyeJitter;
        
        private NonImageBasedMotion _nonImageBasedMotion;
        private EyeBoneAngleMapApplier _angleMapApplier;
        private EyeLookAt _eyeLookAt;

        private Transform _leftEye;
        private Transform _rightEye;
        private bool _hasModel;
        private bool _hasLeftEye;
        private bool _hasRightEye;

        private float _motionScale = 1f;
        private float _motionScaleWithMap = 1f;
        private bool _useAvatarEyeCurveMap = true;
        private IEyeRotationRequestSource[] _sources;

        /// <summary> 目を中央に固定したい場合、毎フレームtrueに設定する </summary>
        public bool ReserveReset { get; set; }
        /// <summary> 目の移動ウェイトを小さくしたい場合、毎フレーム指定する </summary>
        public float ReserveWeight { get; set; } = 1f;

        [Inject]
        public void Initialize(
            IMessageReceiver receiver, IVRMLoadable vrmLoadable,
            IKTargetTransforms ikTargets,　NonImageBasedMotion nonImageBasedMotion)
        {
            _nonImageBasedMotion = nonImageBasedMotion;
            _angleMapApplier = new EyeBoneAngleMapApplier(vrmLoadable);
            _eyeLookAt = new EyeLookAt(vrmLoadable, ikTargets.LookAt);

            vrmLoadable.VrmLoaded += OnVrmLoaded;
            vrmLoadable.VrmDisposing += OnVrmDisposed;
            
            receiver.AssignCommandHandler(
                VmmCommands.SetEyeBoneRotationScale,
                command => _motionScale = command.ParseAsPercentage()
            );

            receiver.AssignCommandHandler(
                VmmCommands.SetEyeBoneRotationScaleWithMap,
                command => _motionScaleWithMap = command.ParseAsPercentage()
            );
            
            receiver.AssignCommandHandler(
                VmmCommands.SetUseAvatarEyeBoneMap,
                command => _useAvatarEyeCurveMap = command.ToBoolean()
            );
            
        }

        private void OnVrmLoaded(VrmLoadedInfo info)
        {
            _leftEye = info.animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEye = info.animator.GetBoneTransform(HumanBodyBones.RightEye);

            //NOTE: せっかく整頓しているので、片目だけボーンがあるモデルでちゃんと動くことを検討する
            _hasLeftEye = _leftEye != null;
            _hasRightEye = _rightEye != null;
            _hasModel = true;
        }

        private void OnVrmDisposed()
        {
            _hasModel = false;
            _hasLeftEye = false;
            _hasRightEye = false;

            _leftEye = null;
            _rightEye = null;
        }

        private void Start()
        {
            //NOTE: 今の計算方式だと考慮不要なはずだが、順番も少しは意識してもいいかも
            _sources = new IEyeRotationRequestSource[]
            {
                _nonImageBasedMotion,
                eyeJitter,
                externalTrackerEyeJitter,
                eyeDownMotionController,
            };
        }
        
        private void LateUpdate()
        {
            if (!_hasModel || (!_hasLeftEye && !_hasRightEye))
            {
                return;
            }

            eyeDownMotionController.UpdateRotationRate();

            var leftRate = Vector2.zero;
            var rightRate = Vector2.zero;

            foreach (var s in _sources)
            {
                if (!s.IsActive)
                {
                    continue;
                }
                leftRate += s.LeftEyeRotationRate;
                rightRate += s.RightEyeRotationRate;
            }

            leftRate = Vector2.ClampMagnitude(leftRate, RateMagnitudeLimit);
            rightRate = Vector2.ClampMagnitude(rightRate, RateMagnitudeLimit);

            // 符号に注意、Unityの普通の座標系ではピッチは下が正
            var leftYaw = leftRate.x * HorizontalRateToAngle;
            var leftPitch = -leftRate.y * VerticalRateToAngle;

            var rightYaw = rightRate.x * HorizontalRateToAngle;
            var rightPitch = -rightRate.y * VerticalRateToAngle;

            _eyeLookAt.Calculate();
            leftYaw += _eyeLookAt.Yaw;
            rightYaw += _eyeLookAt.Yaw;
            leftPitch += _eyeLookAt.Pitch;
            rightPitch += _eyeLookAt.Pitch;

            if (ReserveReset)
            {
                //NOTE: 0でもmap処理が入った結果、非ゼロの角度が入る可能性があるのでreturnはしない
                leftPitch = 0f;
                leftYaw = 0f;
                rightPitch = 0f;
                rightYaw = 0f;
                ReserveReset = false;
            }
            else
            {
                var motionScale = _useAvatarEyeCurveMap
                    ? _motionScaleWithMap
                    : _motionScale * factorWhenMapDisable;
                var weightFactor = motionScale * ReserveWeight;
                leftPitch = ScaleAndClampAngle(leftPitch, weightFactor);
                leftYaw = ScaleAndClampAngle(leftYaw, weightFactor);
                rightPitch = ScaleAndClampAngle(rightPitch, weightFactor);
                rightYaw = ScaleAndClampAngle(rightYaw, weightFactor);
            }
            ReserveWeight = 1f;
            
            if (_useAvatarEyeCurveMap)
            {
                (leftYaw, leftPitch) = _angleMapApplier.GetLeftMappedValues(leftYaw, leftPitch);
                (rightYaw, rightPitch) = _angleMapApplier.GetRightMappedValues(rightYaw, rightPitch);
            }

            if (_hasLeftEye)
            {
                _leftEye.localRotation = Quaternion.Euler(leftPitch, leftYaw, 0f);
            }

            if (_hasRightEye)
            {
                _rightEye.localRotation = Quaternion.Euler(rightPitch, rightYaw, 0f);
            }
        }

        private static float ScaleAndClampAngle(float x, float scale)
        {
            return Mathf.Clamp(x * scale, -AngleAbsLimit, AngleAbsLimit);
        }
    }
}