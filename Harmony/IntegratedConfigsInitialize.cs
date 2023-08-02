using System.Reflection;
using IntegratedConfigs.Scripts;
using JetBrains.Annotations;
using UnityEngine;

namespace IntegratedConfigs.Harmony
{
    [UsedImplicitly]
    public class IntegratedConfigsInitialize : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            // Reduce extra logging
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

            var harmony = new HarmonyLib.Harmony(GetType().ToString());
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            ReflectionHelpers.FindTypesImplementingBase(typeof(IIntegratedConfig), delegate(System.Type type)
            {
                IIntegratedConfig configRegistration = ReflectionHelpers.Instantiate<IIntegratedConfig>(type);
                Log.Out($"{Globals.LOG_TAG} Registering custom XML {configRegistration.RegistrationInfo.XmlName}");
                Harmony_WorldStaticData.RegisterConfig(configRegistration.RegistrationInfo);
            });
        }
    }
}