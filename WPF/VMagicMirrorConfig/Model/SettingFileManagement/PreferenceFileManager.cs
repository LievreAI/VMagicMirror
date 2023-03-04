﻿using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace Baku.VMagicMirrorConfig
{
    class PreferenceFileManager
    {
        public PreferenceFileManager() : this(
            ModelResolver.Instance.Resolve<PreferenceSettingModel>(),
            ModelResolver.Instance.Resolve<HotKeySettingModel>()
            )
        {
        }

        public PreferenceFileManager(PreferenceSettingModel preferenceSetting, HotKeySettingModel hotKeySetting)
        {
            _preferenceSetting = preferenceSetting;
            _hotKeySetting = hotKeySetting;
        }

        private readonly PreferenceSettingModel _preferenceSetting;
        private readonly HotKeySettingModel _hotKeySetting;

        public void Save()
        {
            var data = new PreferenceData()
            {
                HotKeySetting = _hotKeySetting.Save(),
                MinimizeOnLaunch = _preferenceSetting.MinimizeOnLaunch.Value,
            };
            SaveInternal(data);
        }

        public void Load()
        {
            var data = LoadInternal();
            _hotKeySetting.Load(data.HotKeySetting);
            _preferenceSetting.MinimizeOnLaunch.Value = data.MinimizeOnLaunch;
        }

        public void DeleteSaveFile()
        {
            if (File.Exists(SpecialFilePath.PreferenceFilePath))
            {
                File.Delete(SpecialFilePath.PreferenceFilePath);
            }
        }

        private void SaveInternal(PreferenceData data)
        {
            var sb = new StringBuilder();
            using var sw = new StringWriter(sb);
            new JsonSerializer().Serialize(sw, data);
            File.WriteAllText(SpecialFilePath.PreferenceFilePath, sb.ToString());
        }

        
        private PreferenceData LoadInternal()
        {
            if (!File.Exists(SpecialFilePath.PreferenceFilePath))
            {
                var result = PreferenceData.LoadDefault();
                //常にnon-nullではあるが、"!."が嫌いなので…
                if (result.HotKeySetting != null)
                {
                    result.HotKeySetting.IsEmpty = true;
                }
                return result;
            }

            try
            {
                var text = File.ReadAllText(SpecialFilePath.PreferenceFilePath);
                using var sr = new StringReader(text);
                using var jr = new JsonTextReader(sr);
                var jsonSerializer = new JsonSerializer();
                var result = jsonSerializer.Deserialize<PreferenceData>(jr) ?? PreferenceData.LoadDefault();
                return result.Validate();
            }
            catch(Exception ex)
            { 
                LogOutput.Instance.Write(ex);
                return PreferenceData.LoadDefault();
            }

        }
    }
}
