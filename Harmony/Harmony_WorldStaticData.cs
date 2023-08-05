using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using IntegratedConfigs.Core.Entities;
using IntegratedConfigs.Scripts;
using JetBrains.Annotations;
using Noemax.GZip;
using UnityEngine;

namespace IntegratedConfigs.Harmony
{
    public static class Harmony_WorldStaticData
    {
        internal static readonly List<XmlLoadInfo> XmlsToLoad = new List<XmlLoadInfo>();

        internal static void RegisterConfig(XmlLoadInfo xmlLoadInfo)
        {
            XmlsToLoad.Add(xmlLoadInfo);
        }

        internal static IEnumerator LoadSingleXml(
            XmlLoadInfo _loadInfo,
            MemoryStream _memStream,
            DeflateOutputStream _zipStream)
        {
            bool coroutineHadException = false;
            XmlFile xmlFile = null;
            yield return LoadConfig(_loadInfo, (_file => xmlFile = _file));
            if (xmlFile != null)
            {
                if (ThreadManager.IsMainThread() && Application.isPlaying &&
                    SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                {
                    yield return null;
                    string path = GameIO.GetSaveGameDir() + "/ConfigsDump/" + _loadInfo.XmlName + ".xml";
                    string directoryName = Path.GetDirectoryName(path);

                    if (directoryName == null)
                        throw new NullReferenceException($"{Globals.LOG_TAG} LoadSingleXml directoryName was null");
                        
                    if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);
                    File.WriteAllText(path, xmlFile.SerializeToString(), Encoding.UTF8);
                }

                yield return null;
                if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                {
                    yield return ThreadManager.CoroutineWrapperWithExceptionCallback(
                        CacheSingleXml(_loadInfo, xmlFile, _memStream, _zipStream),
                        _exception =>
                        {
                            Log.Error("XML loader: Compressing XML data for '" + xmlFile.Filename + "' failed");
                            Log.Exception(_exception);
                            coroutineHadException = true;
                        });
                    if (coroutineHadException)
                        yield break;
                    else
                        yield return null;
                }

                yield return ThreadManager.CoroutineWrapperWithExceptionCallback(_loadInfo.LoadMethod(xmlFile),
                    _exception =>
                    {
                        Log.Error("XML loader: Loading and parsing '" + xmlFile.Filename + "' failed");
                        Log.Exception(_exception);
                        coroutineHadException = true;
                    });
                if (!coroutineHadException)
                {
                    if (_loadInfo.ExecuteAfterLoad != null)
                    {
                        yield return null;
                        yield return ThreadManager.CoroutineWrapperWithExceptionCallback(
                            _loadInfo.ExecuteAfterLoad(), _exception =>
                            {
                                Log.Error("XML loader: Executing post load step on '" + xmlFile.Filename + "' failed");
                                Log.Exception(_exception);
                                coroutineHadException = true;
                            });
                        if (coroutineHadException)
                            yield break;
                    }

                    Log.Out($"{Globals.LOG_TAG} Loaded (local): {_loadInfo.XmlName}");
                }
            }
        }

        private static IEnumerator LoadConfig(
            XmlLoadInfo _loadInfo,
            Action<XmlFile> _callback)
        {
            var configName = _loadInfo.XmlName;
            if (!configName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                configName += ".xml";
            Exception xmlLoadException = null;
            XmlFile xmlFile = new XmlFile(_loadInfo.DirectoryPath, configName, (_exception =>
                {
                    if (_exception == null)
                        return;
                    xmlLoadException = _exception;
                }));
            while (!xmlFile.Loaded && xmlLoadException == null)
                yield return null;
            if (xmlLoadException != null)
            {
                Log.Error("XML loader: Loading base XML '" + xmlFile.Filename + "' failed:");
                Log.Exception(xmlLoadException);
            }
            else
            {
                _callback(xmlFile);
            }
        }

        private static IEnumerator CacheSingleXml(
            XmlLoadInfo _loadInfo,
            XmlFile _origXml,
            MemoryStream _memStream,
            DeflateOutputStream _zipStream)
        {
            XmlFile clonedXml = new XmlFile(_origXml);
            yield return null;
            clonedXml.RemoveComments();
            yield return null;
            byte[] binXml = clonedXml.SerializeToBytes();
            yield return null;
            _memStream.SetLength(0L);
            _zipStream.Write(binXml, 0, binXml.Length);
            _zipStream.Restart();
            yield return null;
            _loadInfo.CompressedXmlData = _memStream.ToArray();
            _memStream.SetLength(0L);
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("Cleanup", typeof(string))]
    public class CleanupPatches
    {
        [UsedImplicitly]
        private static bool Prefix(string _xmlNameContaining)
        {
            foreach (XmlLoadInfo xmlLoadInfo in Harmony_WorldStaticData.XmlsToLoad)
            {
                if (string.IsNullOrEmpty(_xmlNameContaining) || xmlLoadInfo.XmlName.ContainsCaseInsensitive(_xmlNameContaining))
                {
                    Action cleanupMethod = xmlLoadInfo.CleanupMethod;
                    if (cleanupMethod != null)
                        cleanupMethod();
                }
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("Reset")]
    public static class ResetPatches
    {
        [UsedImplicitly]
        private static void Postfix(string _xmlNameContaining)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                DeflateOutputStream zipStream = new DeflateOutputStream(memoryStream, 3);
                foreach (XmlLoadInfo loadInfo in Harmony_WorldStaticData.XmlsToLoad)
                {
                    if (string.IsNullOrEmpty(_xmlNameContaining) ||
                        loadInfo.XmlName.ContainsCaseInsensitive(_xmlNameContaining))
                    {
                        if (!loadInfo.XmlFileExists())
                        {
                            if (!loadInfo.IgnoreMissingFile)
                                Log.Error("XML loader: XML is missing on reset: " + loadInfo.XmlName);
                        }
                        else
                        {
                            ThreadManager.RunCoroutineSync(
                                Harmony_WorldStaticData.LoadSingleXml(loadInfo, memoryStream, zipStream));
                        }
                    }
                }
            }
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("LoadAllXmlsCo")]
    public class LoadAllXmlsCoPatches
    {
        [UsedImplicitly]
        private static void Postfix(
            bool _isStartup,
            WorldStaticData.ProgressDelegate _progressDelegate)
        {
            Log.Out($"{Globals.LOG_TAG} Loading {Harmony_WorldStaticData.XmlsToLoad.Count.ToString()} custom XMLs");
            using (MemoryStream memStream = new MemoryStream())
            {
                DeflateOutputStream zipStream = new DeflateOutputStream(memStream, 3);
                var xmlLoadInfoArray = Harmony_WorldStaticData.XmlsToLoad;
                for (int index = 0; index < xmlLoadInfoArray.Count; ++index)
                {
                    var loadInfo = xmlLoadInfoArray[index];
                    if (!loadInfo.XmlFileExists())
                    {
                        if (!loadInfo.IgnoreMissingFile)
                            Log.Error("XML loader: XML is missing: " + loadInfo.XmlName);
                    }
                    else if (!_isStartup || loadInfo.LoadAtStartup)
                    {
                        if (_progressDelegate != null && loadInfo.LoadStepLocalizationKey != null)
                        {
                            _progressDelegate(Localization.Get(loadInfo.LoadStepLocalizationKey), 0.0f);
                        }

                        ThreadManager.RunCoroutineSync(
                            Harmony_WorldStaticData.LoadSingleXml(loadInfo, memStream, zipStream));
                        
                        // ReSharper disable once RedundantAssignment
                        loadInfo = null;
                    }
                }

                // ReSharper disable once RedundantAssignment
                xmlLoadInfoArray = null;
            }
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("SendXmlsToClient")]
    public static class SendXmlsToClientPatches
    {
        [UsedImplicitly]
        private static void Postfix(ClientInfo _cInfo)
        {
            foreach (XmlLoadInfo xmlLoadInfo in Harmony_WorldStaticData.XmlsToLoad)
            {
                if (xmlLoadInfo.SendToClients && (xmlLoadInfo.LoadClientFile || xmlLoadInfo.CompressedXmlData != null))
                    _cInfo.SendPackage(NetPackageManager.GetPackage<NetPackageConfigFile>().Setup(xmlLoadInfo.XmlName, 
                        xmlLoadInfo.LoadClientFile ? null : xmlLoadInfo.CompressedXmlData));
            }
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("AllConfigsReceivedAndLoaded")]
    public static class AllConfigsReceivedAndLoadedPatches
    {
        [UsedImplicitly]
        // ReSharper disable once RedundantAssignment
        private static bool Prefix(ref bool __result, ref Coroutine ___receivedConfigsHandlerCoroutine)
        {
            __result = (___receivedConfigsHandlerCoroutine == null &&
                        WaitForConfigsFromServerPatches.ReceivedConfigsHandlerCoroutine == null);
            return false;
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("WaitForConfigsFromServer")]
    public static class WaitForConfigsFromServerPatches
    {
        public static int HighestReceivedIndex = -1;
        public static Coroutine ReceivedConfigsHandlerCoroutine;
        
        [UsedImplicitly]
        private static bool Prefix(ref Coroutine ___receivedConfigsHandlerCoroutine)
        {
            ReceivedConfigsHandlerCoroutine = ___receivedConfigsHandlerCoroutine;
            if (ReceivedConfigsHandlerCoroutine != null)
                ThreadManager.StopCoroutine(ReceivedConfigsHandlerCoroutine);
            ReceivedConfigsHandlerCoroutine = ThreadManager.StartCoroutine(HandleReceivedConfigs());

            return true;
        }

        private static IEnumerator HandleReceivedConfigs()
        {
            HighestReceivedIndex = -1;
            foreach (XmlLoadInfo xmlLoadInfo in Harmony_WorldStaticData.XmlsToLoad)
                xmlLoadInfo.WasReceivedFromServer = ClientFileState.None;
            
            while (string.IsNullOrEmpty(GamePrefs.GetString(EnumGamePrefs.GameWorld)))
                yield return null;
            
            int waitingFor = 0;
            while (waitingFor < Harmony_WorldStaticData.XmlsToLoad.Count)
            {
                XmlLoadInfo loadInfo = Harmony_WorldStaticData.XmlsToLoad[waitingFor];
                if (!loadInfo.SendToClients)
                    ++waitingFor;
                else if (loadInfo.WasReceivedFromServer == ClientFileState.None)
                {
                    if (loadInfo.IgnoreMissingFile && HighestReceivedIndex > waitingFor)
                        ++waitingFor;
                    else
                        yield return null;
                }
                else
                {
                    WorldStaticData.Cleanup(loadInfo.XmlName);
                    if (loadInfo.WasReceivedFromServer == ClientFileState.LoadLocal)
                    {
                        yield return Harmony_WorldStaticData.LoadSingleXml(loadInfo, null, null);
                    }
                    else
                    {
                        byte[] uncompressedData;
                        using (MemoryStream input = new MemoryStream(loadInfo.CompressedXmlData))
                        {
                            using (DeflateInputStream source = new DeflateInputStream(input))
                            {
                                using (MemoryStream destination = new MemoryStream())
                                {
                                    StreamUtils.StreamCopy(source, destination);
                                    uncompressedData = destination.ToArray();
                                }
                            }
                        }

                        yield return null;
                        XmlFile xmlFile = new XmlFile(uncompressedData);
                        yield return null;
                        bool coroutineHadException = false;
                        yield return ThreadManager.CoroutineWrapperWithExceptionCallback(
                            loadInfo.LoadMethod(xmlFile), _exception =>
                            {
                                Log.Error("XML loader: Loading and parsing '" + xmlFile.Filename + "' failed");
                                Log.Exception(_exception);
                                coroutineHadException = true;
                            });
                        if (!coroutineHadException)
                        {
                            if (loadInfo.ExecuteAfterLoad != null)
                            {
                                yield return null;
                                yield return ThreadManager.CoroutineWrapperWithExceptionCallback(
                                    loadInfo.ExecuteAfterLoad(), _exception =>
                                    {
                                        Log.Error("XML loader: Executing post load step on '" + xmlFile.Filename +
                                                  "' failed");
                                        Log.Exception(_exception);
                                        coroutineHadException = true;
                                    });
                                if (coroutineHadException)
                                    continue;
                            }

                            Log.Out($"{Globals.LOG_TAG} Loaded (received): {loadInfo.XmlName}");
                            yield return null;
                            // ReSharper disable once RedundantAssignment
                            uncompressedData = null;
                        }
                        else
                            continue;
                    }

                    ++waitingFor;
                    // ReSharper disable once RedundantAssignment
                    loadInfo = null;
                }
            }

            ReceivedConfigsHandlerCoroutine = null;
        }
    }
    
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("ReceivedConfigFile")]
    public static class ReceivedConfigFilePatches
    {
        [UsedImplicitly]
        private static bool Prefix(string _name, byte[] _data)
        {
            if (_data != null)
                Log.Out(string.Format("Received config file '{0}' from server. Len: {1}", _name, _data.Length.ToString()));
            else
                Log.Out("Loading config '" + _name + "' from local files");
            int _arrayIndex;
            XmlLoadInfo loadInfoForName = GetLoadInfoForName(_name, out _arrayIndex);
            if (loadInfoForName != null)
            {
                Log.Out("XML loader: Received config: " + _name);
                loadInfoForName.CompressedXmlData = _data;
                loadInfoForName.WasReceivedFromServer = _data != null ? ClientFileState.Received : ClientFileState.LoadLocal;
                WaitForConfigsFromServerPatches.HighestReceivedIndex = MathUtils.Max(WaitForConfigsFromServerPatches.HighestReceivedIndex, _arrayIndex);
                
                Log.Out($"{Globals.LOG_TAG} XML Source was {loadInfoForName.WasReceivedFromServer.ToString()}");
                
                // Since we got a config file that this patch handles, don't call vanilla code handling.
                return false;
            }

            return true;
        }
        
        private static XmlLoadInfo GetLoadInfoForName(
            string _xmlName,
            out int _arrayIndex)
        {
            _arrayIndex = -1;
            for (int index = 0; index < Harmony_WorldStaticData.XmlsToLoad.Count; ++index)
            {
                XmlLoadInfo loadInfoForName = Harmony_WorldStaticData.XmlsToLoad[index];
                if (loadInfoForName.XmlName.EqualsCaseInsensitive(_xmlName))
                {
                    _arrayIndex = index;
                    return loadInfoForName;
                }
            }
            return null;
        }
    }
    
    /// <summary>
    /// Not Implementing [via a20.7]
    /// Vanilla uses this to save configs to a folder for viewing. It is triggered by a console command
    /// so its value is limited.
    /// </summary>
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("SaveXmlsToFolder")]
    public static class SaveXmlsToFolderPatches { }
    
    /// <summary>
    /// Not Implementing [via a20.7]
    /// Vanilla does not seem to call this function.
    /// </summary>
    [HarmonyPatch(typeof(WorldStaticData))]
    [HarmonyPatch("ReloadInGameXML")]
    public static class ReloadInGameXMLPatches { }
}