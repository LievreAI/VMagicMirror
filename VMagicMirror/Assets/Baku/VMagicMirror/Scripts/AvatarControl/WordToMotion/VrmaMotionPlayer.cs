using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UniVRM10;
using Zenject;

namespace Baku.VMagicMirror
{
    public class VrmaMotionPlayer : PresenterBase, ILateTickable, IWordToMotionPlayer
    {
        //NOTE: Toesが任意ボーンなことに注意
        private static readonly HumanBodyBones[] LowerBodyBones = {
            HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
            HumanBodyBones.RightToes,
        };
        
        public VrmaMotionPlayer(VrmaRepository repository, IVRMLoadable vrmLoadable)
        {
            _repository = repository;
            _vrmLoadable = vrmLoadable;
        }
        
        private readonly IVRMLoadable _vrmLoadable;
        private readonly VrmaRepository _repository;

        private bool _hasModel;
        private Vrm10Runtime _vrm10Runtime;
        private readonly Dictionary<HumanBodyBones, Transform> _lowerBodyBones = new();
        private readonly Dictionary<HumanBodyBones, Quaternion> _lowerBodyBoneRotationCache = new();
        private Vector3 _hipLocalPositionCache;

        private bool _playing;
        private bool _playingPreview;

        public override void Initialize()
        {
            _vrmLoadable.VrmLoaded += OnModelLoaded;
            _vrmLoadable.VrmDisposing += OnModelDisposed;
        }

        private void OnModelLoaded(VrmLoadedInfo info)
        {
            _lowerBodyBones.Clear();
            _lowerBodyBoneRotationCache.Clear();
            var animator = info.animator;
            foreach (var bone in LowerBodyBones)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                {
                    _lowerBodyBones[bone] = t;
                    //NOTE: 値を一応入れてるが、別に無くても破綻はしないつもり
                    _lowerBodyBoneRotationCache[bone] = Quaternion.identity;
                }
            }
            _hasModel = true;
        }
        
        private void OnModelDisposed()
        {
            _hasModel = false;
            _lowerBodyBones.Clear();
            _lowerBodyBoneRotationCache.Clear();
        }
        
        void ILateTickable.LateTick()
        {
            if (!_playing && !_playingPreview)
            {
                return;
            }

            var anim = _repository.PeekInstance;
            if (anim == null)
            {
                //普通ここは通らない
                return;
            }

            //Retarget処理はUniVRM実装に頼ったほうが無難なので使う + それはそうと腰から下は動かさないのがWord to Motionの期待値なのでそうしている
            CacheLowerBodyTransforms();
            Vrm10Retarget.Retarget(
                anim.Instance.ControlRig, (_vrm10Runtime.ControlRig, _vrm10Runtime.ControlRig)
                );
            RestoreLowerBodyTransforms();
        }

        private void CacheLowerBodyTransforms()
        {
            foreach (var pair in _lowerBodyBones)
            {
                _lowerBodyBoneRotationCache[pair.Key] = pair.Value.localRotation;
            }
            _hipLocalPositionCache = _lowerBodyBones[HumanBodyBones.Hips].localPosition;
        }

        private void RestoreLowerBodyTransforms()
        {
            foreach (var pair in _lowerBodyBoneRotationCache)
            {
                _lowerBodyBones[pair.Key].localRotation = pair.Value;
            }
            _lowerBodyBones[HumanBodyBones.Hips].localPosition = _hipLocalPositionCache;
        }
        
        //NOTE: モーション名として拡張子付きのファイル名が使われている事を期待している
        private VrmaFileItem FindFileItem(string motionName)
        {
            return _repository
                .GetAvailableFileItems()
                .FirstOrDefault(i => string.Compare(
                        motionName, 
                        i.FileName,
                        StringComparison.InvariantCultureIgnoreCase
                    ) 
                    == 0
                );
        }

        bool IWordToMotionPlayer.UseIkAndFingerFade => true;

        bool IWordToMotionPlayer.CanPlay(MotionRequest request)
        {
            var targetItem = FindFileItem(request.CustomMotionClipName);
            return targetItem.IsValid;
        }

        void IWordToMotionPlayer.Play(MotionRequest request, out float duration)
        {
            var targetItem = FindFileItem(request.CustomMotionClipName);
            if (!targetItem.IsValid)
            {
                duration = 1f;
                return;
            }
            _repository.Run(targetItem, false, out duration);
            _playing = true;
        }

        //TODO: Game InputでもVRMAを使う場合、この方法で止めるのはNG
        void IWordToMotionPlayer.Stop()
        {
            _repository.Stop();
            _playing = false;
        }

        void IWordToMotionPlayer.PlayPreview(MotionRequest request)
        {
            var targetItem = FindFileItem(request.CustomMotionClipName);
            if (!targetItem.IsValid)
            {
                return;
            }
            _repository.Run(targetItem, true, out _);
            _playingPreview = true;
            _playing = false;
        }

        //TODO: Stop()と同じ
        void IWordToMotionPlayer.StopPreview()
        {
            _repository.Stop();
            _playing = false;
            _playingPreview = false;
        }

    }
}
