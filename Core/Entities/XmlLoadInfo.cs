using System;
using System.Collections;
using System.IO;
using JetBrains.Annotations;

namespace IntegratedConfigs.Core.Entities
{
    public enum ClientFileState
    {
        None,
        Received,
        LoadLocal,
    }
    
    public class XmlLoadInfo
    {
        public readonly string XmlName;
        public readonly string DirectoryPath;
        public readonly string LoadStepLocalizationKey;
        public readonly bool LoadAtStartup;
        public readonly bool SendToClients;
        public readonly bool IgnoreMissingFile;
        public readonly bool AllowReloadDuringGame;
        public readonly Func<XmlFile, IEnumerator> LoadMethod;
        public readonly Action CleanupMethod;
        public readonly Func<IEnumerator> ExecuteAfterLoad;
        public readonly Action<XmlFile> ReloadDuringGameMethod;
        public byte[] CompressedXmlData;
        [UsedImplicitly] public bool LoadClientFile;
        public ClientFileState WasReceivedFromServer;

        public bool XmlFileExists() => File.Exists($"{DirectoryPath}/{XmlName}.xml");

        public XmlLoadInfo(
            string xmlName,
            string directoryPath,
            bool loadAtStartup,
            bool sendToClients,
            Func<XmlFile, IEnumerator> loadMethod,
            Action cleanupMethod,
            Func<IEnumerator> executeAfterLoad = null,
            bool allowReloadDuringGame = false,
            Action<XmlFile> reloadDuringGameMethod = null,
            bool ignoreMissingFile = false,
            string loadStepLocalizationKey = null)
        {
            XmlName = xmlName;
            DirectoryPath = directoryPath;
            LoadAtStartup = loadAtStartup;
            SendToClients = sendToClients;
            LoadMethod = loadMethod;
            CleanupMethod = cleanupMethod;
            ExecuteAfterLoad = executeAfterLoad;
            AllowReloadDuringGame = allowReloadDuringGame;
            ReloadDuringGameMethod = reloadDuringGameMethod;
            IgnoreMissingFile = ignoreMissingFile;
            LoadStepLocalizationKey = loadStepLocalizationKey;
            
            if (LoadMethod == null) throw new ArgumentNullException(nameof(loadMethod));
        }
    }
}