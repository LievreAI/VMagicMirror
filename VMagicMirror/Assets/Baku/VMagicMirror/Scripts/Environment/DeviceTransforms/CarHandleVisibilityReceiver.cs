﻿using System;
using Deform;
using DG.Tweening;
using UnityEngine;
using Zenject;

namespace Baku.VMagicMirror
{
    [RequireComponent(typeof(MagnetDeformer))]
    public class CarHandleVisibilityReceiver : MonoBehaviour
    {
        //TODO: 非MonoBehaviour化できそう
        //TODO: ほんとはArcadeStick版と差分コーディングしたほうがいい(ほぼ同じ実装なので)
        [Inject]
        public void Initialize(IMessageReceiver receiver, DeformableCounter deformableCounter)
        {
            _deformableCounter = deformableCounter;
            receiver.AssignCommandHandler(
                VmmCommands.GamepadVisibility,
                message =>
                {
                    _gamepadDeviceVisible = message.ToBoolean();
                    SetGamepadVisibility();
                });
            receiver.AssignCommandHandler(
                VmmCommands.SetGamepadMotionMode,
                v =>
                {
                    _gamepadMotionMode = (GamepadMotionModes) v.ToInt();
                    SetGamepadVisibility();
                });
        }

        private bool _gamepadDeviceVisible;
        private GamepadMotionModes _gamepadMotionMode = GamepadMotionModes.Gamepad;

        private DeformableCounter _deformableCounter;
        private MagnetDeformer _deformer = null;
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private bool _latestVisibility = false;
        
        public bool IsVisible => _latestVisibility;

        private void Start()
        {
            _deformer = GetComponent<MagnetDeformer>();
            _renderers = GetComponentsInChildren<Renderer>();
        }

        private void SetGamepadVisibility()
        {
            bool visible = _gamepadDeviceVisible && _gamepadMotionMode is GamepadMotionModes.CarController;
            if (visible == _latestVisibility)
            {
                return;
            }

            _latestVisibility = visible;
            DOTween
                .To(
                    () => _deformer.Factor, 
                    v => _deformer.Factor = v, 
                    visible ? 0.0f : 1.0f, 
                    0.5f)
                .SetEase(Ease.OutCubic)
                .OnStart(() =>
                {
                    _deformableCounter.Increment();
                    if (visible)
                    {
                        foreach (var r in _renderers)
                        {
                            r.enabled = true;
                        }
                    }
                })
                .OnComplete(() =>
                {
                    _deformableCounter.Decrement();
                    foreach (var r in _renderers)
                    {
                        r.enabled = _latestVisibility;
                    }
                });
        }
    }
}
