using Newtonsoft.Json.Linq;
using System.IO;
using System.Security.Cryptography; // Для MD5
using System.Text;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System;

namespace Comfizen
{
    public class SessionData
    {
        public JObject ApiState { get; set; }
        public ObservableCollection<WorkflowGroup> GroupsState { get; set; }
    }

    public class SessionManager
    {
        private readonly AppSettings _settings;
        
        private static readonly JsonSerializer _fingerprintSerializer = new JsonSerializer
        {
            Formatting = Formatting.None,
            ContractResolver = new OrderedContractResolver()
        };

        private class OrderedContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(System.Type type, MemberSerialization memberSerialization)
            {
                return base.CreateProperties(type, memberSerialization).OrderBy(p => p.PropertyName).ToList();
            }
        }

        public SessionManager(AppSettings settings)
        {
            _settings = settings;
            Directory.CreateDirectory(_settings.SessionsDirectory);
        }

        private string GetSessionFileName(string workflowFullPath)
        {
            string pathToHash = Path.GetFullPath(workflowFullPath).ToLowerInvariant();
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(pathToHash);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                var sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString() + ".json";
            }
        }
        
        /// <summary>
        /// Generates a unique MD5 fingerprint based on both the UI structure and the scripts.
        /// </summary>
        /// <param name="uiDefinition">The collection of workflow groups (promptTemplate).</param>
        /// <param name="scripts">The collection of scripts.</param>
        /// <returns>A unique MD5 hash string.</returns>
        public string GenerateFingerprint(ObservableCollection<WorkflowGroup> uiDefinition, ScriptCollection scripts)
        {
            if (uiDefinition == null) return string.Empty;

            using (var sw = new StringWriter())
            {
                // The fingerprint is now based on a combined object containing both UI and scripts.
                var dataToHash = new { promptTemplate = uiDefinition, scripts = scripts };
                _fingerprintSerializer.Serialize(sw, dataToHash);
                string jsonText = sw.ToString();

                using (MD5 md5 = MD5.Create())
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(jsonText);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);
                    var sb = new StringBuilder();
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        sb.Append(hashBytes[i].ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
        }

        /// <summary>
        /// Scans workflow files to find one that matches the given fingerprint (UI + scripts).
        /// </summary>
        /// <param name="fingerprint">The fingerprint to search for.</param>
        /// <returns>The full path to the matching workflow file, or null if not found.</returns>
        public string FindWorkflowByFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint) || !Directory.Exists(Workflow.WorkflowsDir))
            {
                return null;
            }
    
            var files = Directory.EnumerateFiles(Workflow.WorkflowsDir, "*.json", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    var jsonString = File.ReadAllText(file);
                    
                    // Deserialize both promptTemplate and scripts from the file.
                    var data = JsonConvert.DeserializeAnonymousType(jsonString, 
                        new { 
                            promptTemplate = default(ObservableCollection<WorkflowGroup>),
                            scripts = default(ScriptCollection) 
                        });

                    if (data.promptTemplate != null)
                    {
                        // Generate the fingerprint using both parts.
                        // Use an empty ScriptCollection for old files that don't have a scripts block.
                        var currentFingerprint = GenerateFingerprint(data.promptTemplate, data.scripts ?? new ScriptCollection());
                        if (fingerprint.Equals(currentFingerprint, System.StringComparison.OrdinalIgnoreCase))
                        {
                            return Path.GetFullPath(file);
                        }
                    }
                }
                catch
                {
                    // Ignore corrupted or invalid files
                }
            }

            return null;
        }

        public void SaveSession(JObject workflowJObject, ObservableCollection<WorkflowGroup> groups, string workflowFullPath)
        {
            if (string.IsNullOrEmpty(workflowFullPath) || workflowJObject == null) return;

            var sessionData = new SessionData
            {
                ApiState = workflowJObject,
                GroupsState = groups
            };

            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            var json = JsonConvert.SerializeObject(sessionData, Formatting.Indented);
            File.WriteAllText(sessionFilePath, json);
        }

        public SessionData? LoadSession(string workflowFullPath)
        {
            if (string.IsNullOrEmpty(workflowFullPath)) return null;
            
            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            if (File.Exists(sessionFilePath))
            {
                string jsonString = File.ReadAllText(sessionFilePath);
                try
                {
                    var sessionData = JsonConvert.DeserializeObject<SessionData>(jsonString);
                    if (sessionData != null && sessionData.ApiState == null && sessionData.GroupsState == null)
                    {
                        var oldApiState = JObject.Parse(jsonString);
                        return new SessionData { ApiState = oldApiState, GroupsState = null };
                    }
                    return sessionData;
                }
                catch (JsonSerializationException)
                {
                    try
                    {
                        var oldApiState = JObject.Parse(jsonString);
                        return new SessionData { ApiState = oldApiState, GroupsState = null };
                    }
                    catch
                    {
                        return null; 
                    }
                }
                catch
                {
                    return null; 
                }
            }
            return null;
        }

        public void ClearSession(string workflowFullPath)
        {
            if (string.IsNullOrEmpty(workflowFullPath)) return;
            
            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            if (File.Exists(sessionFilePath))
            {
                File.Delete(sessionFilePath);
            }
        }
    }
}