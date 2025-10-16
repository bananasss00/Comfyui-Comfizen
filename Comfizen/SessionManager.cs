using Newtonsoft.Json.Linq;
using System.IO;
using System.Security.Cryptography; // Для MD5
using System.Text;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;

namespace Comfizen
{
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
        
        public string GenerateFingerprint(ObservableCollection<WorkflowGroup> uiDefinition)
        {
            if (uiDefinition == null) return string.Empty;

            using (var sw = new StringWriter())
            {
                // The fingerprint is based ONLY on the UI structure (promptTemplate)
                _fingerprintSerializer.Serialize(sw, uiDefinition);
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
                    var data = JsonConvert.DeserializeAnonymousType(jsonString, 
                        new { promptTemplate = default(ObservableCollection<WorkflowGroup>) });

                    if (data.promptTemplate != null)
                    {
                        var currentFingerprint = GenerateFingerprint(data.promptTemplate);
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

        public void SaveSession(JObject workflowJObject, string workflowFullPath)
        {
            if (workflowJObject == null) return;

            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            File.WriteAllText(sessionFilePath, workflowJObject.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public JObject? LoadSession(string workflowFullPath)
        {
            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            if (File.Exists(sessionFilePath))
            {
                string jsonString = File.ReadAllText(sessionFilePath);
                return JObject.Parse(jsonString);
            }
            return null;
        }

        public void ClearSession(string workflowFullPath)
        {
            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            if (File.Exists(sessionFilePath))
            {
                File.Delete(sessionFilePath);
            }
        }
    }
}