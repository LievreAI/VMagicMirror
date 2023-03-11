using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UniVRM10;
using Zenject;

namespace Baku.VMagicMirror
{
    public class AnimMorphEasedTargetV2 : MonoBehaviour
    {
        [Tooltip("主要な母音音素(aa, E, ih, oh, ou)に対してBlendShapeを動かすカーブ")]
        public AnimationCurve transitionCurves = new AnimationCurve(new[]
        {
            new Keyframe(0.0f, 0.0f),
            new Keyframe(0.1f, 1.0f),
        });

        [Tooltip("発音しなくなった母音のBlendShapeをゼロに近づける際の速度を表す値")]
        public float cancelSpeedFactor = 8.0f;

        [Range(0.0f, 100.0f), Tooltip("この閾値未満の音素の重みは無視する")]
        public float weightThreshold = 2.0f;

        [Tooltip("OVRLipSyncに渡すSmoothing amountの値")]
        public int smoothAmount = 65;
        
        private bool _shouldReceiveData = true;
        public bool ShouldReceiveData
        {
            get => _shouldReceiveData;
            set
            {
                if (_shouldReceiveData == value)
                {
                    return;
                }

                _shouldReceiveData = value;
                if (!value)
                {
                    UpdateToClosedMouth();
                }
            }
        }

        private readonly RecordLipSyncSource _lipSyncSource = new RecordLipSyncSource();
        public IMouthLipSyncSource LipSyncSource => _lipSyncSource;

        private readonly Dictionary<ExpressionKey, float> _blendShapeWeights = new Dictionary<ExpressionKey, float>
        {
            [ExpressionKey.Aa] = 0f,
            [ExpressionKey.Ee] = 0f,
            [ExpressionKey.Ih] = 0f,
            [ExpressionKey.Oh] = 0f,
            [ExpressionKey.Ou] = 0f,
        };

        private readonly ExpressionKey[] _keys = {
            ExpressionKey.Aa,
            ExpressionKey.Ee, 
            ExpressionKey.Ih, 
            ExpressionKey.Oh, 
            ExpressionKey.Ou, 
        };

        private VmmLipSyncContextBase _context;
        private bool _adjustLipSyncByVolume = true;
        private OVRLipSync.Viseme _previousViseme = OVRLipSync.Viseme.sil;
        private float _transitionTimer = 0.0f;

        [Inject]
        public void Initialize(IMessageReceiver receiver)
        {
            receiver.AssignCommandHandler(
                VmmCommands.AdjustLipSyncByVolume,
                command => _adjustLipSyncByVolume = command.ToBoolean());
        }
        
        private void Start()
        {
            _context = GetComponent<VmmLipSyncContextBase>();
            if (_context == null)
            {
                LogOutput.Instance.Write("同じGameObjectにOVRLipSyncContextBaseを継承したクラスが見つかりません。");
            }
            _context.Smoothing = smoothAmount;
        }

        private float[] weights = new float[5];
        private void Update()
        {
            if (Application.isEditor)
            {
                _context.Smoothing = smoothAmount;
            }
            
            //口閉じの場合: とにかく閉じるのが良いので閉じて終わり
            if (!ShouldReceiveData)
            {
                UpdateToClosedMouth();
                return;
            }

            if (!_context.enabled || !(_context.GetCurrentPhonemeFrame() is { } frame))
            {
                return;
            }

            _transitionTimer += Time.deltaTime;

            // 最大の重みを持つ音素を探す。子音は無視
            var maxVisemeIndex = 0;
            var maxVisemeWeight = 0.0f;

            var weightIndex = 0;
            for (var i = (int)OVRLipSync.Viseme.aa; i < frame.Visemes.Length; i++)
            {
                if (frame.Visemes[i] > maxVisemeWeight)
                {
                    maxVisemeWeight = frame.Visemes[i];
                    maxVisemeIndex = i;
                }

                weights[weightIndex] = frame.Visemes[i];
                weightIndex++;
            }

            Debug.Log(
                $"{Time.frameCount}, v={(OVRLipSync.Viseme)maxVisemeIndex}, w=({string.Join(",", weights.Select(w => (int)(w * 100)))})"
                );

            if (maxVisemeIndex == 0)
            {
                foreach (var k in _keys)
                {
                    _blendShapeWeights[k] = 0f;
                }
            }
            else
            {
                var index = maxVisemeIndex - (int)OVRLipSync.Viseme.aa;
                for (var i = 0; i < _keys.Length; i++)
                {
                    _blendShapeWeights[_keys[i]] =
                        i == index ? maxVisemeWeight : 0f;
                }
            }
            
            // 音素の重みが小さすぎる場合は口を閉じる
            if (maxVisemeWeight * 100.0f < weightThreshold)
            {
                _transitionTimer = 0.0f;
            }

            // 音素の切り替わりでタイマーをリセットする
            if (_previousViseme != (OVRLipSync.Viseme)maxVisemeIndex)
            {
                _transitionTimer = 0.0f;
                _previousViseme = (OVRLipSync.Viseme)maxVisemeIndex;
            }

            int visemeIndex = maxVisemeIndex - (int)OVRLipSync.Viseme.aa;
            bool hasValidMaxViseme = (visemeIndex >= 0);

            for(int i = 0; i < _keys.Length; i++)
            {
                var key = _keys[i];

                _blendShapeWeights[key] = Mathf.Lerp(
                    _blendShapeWeights[key],
                    0.0f,
                    Time.deltaTime * cancelSpeedFactor
                    );

                //減衰中の値のほうが大きければそっちを採用する。
                //「あぁあぁぁあ」みたいな声の出し方した場合にEvaluateだけ使うとヘンテコになる可能性が高い為。
                if (hasValidMaxViseme && i == visemeIndex)
                {
                    _blendShapeWeights[key] = Mathf.Max(
                        _blendShapeWeights[key],
                        transitionCurves.Evaluate(_transitionTimer)
                        );
                }
            }

            var factor = _adjustLipSyncByVolume ? GetLipSyncFactorByVolume() : 1f;
            //順番に注意: visemeのキーに合わせてます
            _lipSyncSource.A = _blendShapeWeights[_keys[0]] * factor;
            _lipSyncSource.E = _blendShapeWeights[_keys[1]] * factor;
            _lipSyncSource.I = _blendShapeWeights[_keys[2]] * factor;
            _lipSyncSource.O = _blendShapeWeights[_keys[3]] * factor;
            _lipSyncSource.U = _blendShapeWeights[_keys[4]] * factor;
        }

        private float GetLipSyncFactorByVolume()
        {
            //-40dBで口が開きはじめ、-20dBになると完全に開く
            const int MinLevel = 10;
            const int MaxLevel = 30;
            
            var rawResult = 
                Mathf.Clamp(_context.CurrentVolumeLevel - MinLevel, 0, MaxLevel - MinLevel) * 1.0f / (MaxLevel - MinLevel);

            if (_context.CurrentVolumeLevel > MinLevel)
            {
                return Mathf.Clamp(rawResult, 0.3f, 1f);
            }
            else
            {
                //ここでパキっと0になることはそんなに多くなくて、動くの渋いかなあ…うーん…
                return 0f;
            }
        }
        
        private void UpdateToClosedMouth()
        {
            foreach(var key in _keys)
            {
                _blendShapeWeights[key] = 0.0f;
            }

            _lipSyncSource.A = 0;
            _lipSyncSource.I = 0;
            _lipSyncSource.U = 0;
            _lipSyncSource.E = 0;
            _lipSyncSource.O = 0;
        }
    }
}
