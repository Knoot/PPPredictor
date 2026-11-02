using BeatSaberMarkupLanguage;
using HMUI;
using IPA.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PPPredictor.Core;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Data;
using PPPredictor.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace PPPredictor.Utilities
{
    class ProfileInfoMgr
    {
        internal static FlowCoordinator ParentFlow { get; private set; }
        private static PPPredictorFlowCoordinator _flow;
        private static readonly string profilePath = Path.Combine(UnityGame.UserDataPath, "PPPredictorProfileInfo.json");
        private static readonly int _profileInfoVersion = 6;
        private static readonly AtomicJsonStore store = new AtomicJsonStore(profilePath);
        private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        private sealed class SettingsOnlyResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization serialization)
            {
                var property = base.CreateProperty(member, serialization);
                if (member.DeclaringType == typeof(ProfileInfo) && member.Name == nameof(ProfileInfo.DctleaderBoardData)) property.Ignored = true;
                return property;
            }
        }

        private static ProfileInfo CopySettings(ProfileInfo profile) => JsonConvert.DeserializeObject<ProfileInfo>(
            JsonConvert.SerializeObject(profile, new JsonSerializerSettings { ContractResolver = new SettingsOnlyResolver() }));
        internal static ProfileInfo LoadProfileInfo()
        {
            var info = store.Load(json => JsonConvert.DeserializeObject<ProfileInfo>(json),
                (path, error) => Plugin.Log?.Warn($"Unable to load profile {path}: {error.Message}"));
            if (info == null) return new ProfileInfo();
            if (info.ProfileInfoVersion < _profileInfoVersion) info.ResetCachedData();
            return info;
        }

        internal static Task<bool> SaveCurrentProfileAsync(ProfileInfo profile, CalculatorInstance calculator)
        {
            // Capture mutable UI settings before moving snapshot creation and disk I/O off the Unity thread.
            var snapshot = CopySettings(profile);
            long version = store.ReserveVersion();
            return Task.Run(() => SaveSnapshot(snapshot, calculator.GetSaveData(), calculator.LastSessionReset, version));
        }

        internal static bool SaveProfile(ProfileInfo profile, Dictionary<string, LeaderboardData> data)
        {
            return SaveSnapshot(CopySettings(profile), data, profile.LastSessionReset, store.ReserveVersion());
        }

        private static bool SaveSnapshot(ProfileInfo snapshot, Dictionary<string, LeaderboardData> data, DateTime sessionReset, long version)
        {
            try
            {
                snapshot.DctleaderBoardData = data;
                snapshot.LastSessionReset = sessionReset;
                snapshot.ProfileInfoVersion = _profileInfoVersion;
                snapshot.ClearOldMapInfos();
                store.Write(version, JsonConvert.SerializeObject(snapshot, Formatting.Indented, jsonSettings));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.ErrorPrint($"Error saving profile; previous file retained: {ex.Message}");
                return false;
            }
        }

        internal static async void ResetSettings()
        {
            Plugin.ProfileInfo = new ProfileInfo();
            PPPredictorMgr.CalculatorInstance.AssignSettings(await Plugin.ProfileInfo.ParseToSetting());
        }

        internal static void ResetCache()
        {
            Plugin.ProfileInfo.ResetCachedData();
            SaveProfile(Plugin.ProfileInfo, Plugin.ProfileInfo.DctleaderBoardData);
        }

        public static void ShowSettingsFlow()
        {
            if (_flow == null)
                _flow = BeatSaberUI.CreateFlowCoordinator<PPPredictorFlowCoordinator>();

            ParentFlow = BeatSaberUI.MainFlowCoordinator.YoungestChildFlowCoordinatorOrSelf();

            BeatSaberUI.PresentFlowCoordinator(ParentFlow, _flow, immediately: true);
        }
    }
}
