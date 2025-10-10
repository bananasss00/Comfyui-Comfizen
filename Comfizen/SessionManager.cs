using Newtonsoft.Json.Linq;
using System.IO;
using System.Security.Cryptography; // Для MD5
using System.Text;

namespace Comfizen
{
    public class SessionManager
    {
        private readonly AppSettings _settings;

        public SessionManager(AppSettings settings)
        {
            _settings = settings;
            // Убеждаемся, что директория для сессий существует
            Directory.CreateDirectory(_settings.SessionsDirectory);
        }

        /// <summary>
        /// Генерирует уникальное имя файла для сессии на основе содержимого Workflow.
        /// Это гарантирует, что сессии не будут конфликтовать, даже если workflow названы одинаково,
        /// но имеют разное внутреннее наполнение.
        /// </summary>
        private string GetSessionFileName(string workflowFullPath)
        {
            // ========================================================== //
            //     НАЧАЛО ИСПРАВЛЕНИЯ 3 (Улучшение)                       //
            // ========================================================== //
            // Приводим путь к нижнему регистру перед хэшированием,
            // чтобы C:\file.json и c:\file.json имели один и тот же файл сессии.
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
            // ========================================================== //
            //     КОНЕЦ ИСПРАВЛЕНИЯ 3                                    //
            // ========================================================== //
        }

        /// <summary>
        /// Сохраняет текущее состояние JObject workflow в файл сессии.
        /// </summary>
        /// <param name="workflowJObject">JObject для сохранения.</param>
        /// <param name="workflowFullPath">Полный путь к файлу workflow, для которого сохраняется сессия.</param>
        public void SaveSession(JObject workflowJObject, string workflowFullPath)
        {
            if (workflowJObject == null) return;

            string sessionFileName = GetSessionFileName(workflowFullPath);
            string sessionFilePath = Path.Combine(_settings.SessionsDirectory, sessionFileName);

            File.WriteAllText(sessionFilePath, workflowJObject.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>
        /// Загружает состояние JObject workflow из файла сессии.
        /// </summary>
        /// <param name="workflowFullPath">Полный путь к файлу workflow, для которого загружается сессия.</param>
        /// <returns>Загруженный JObject или null, если файл сессии не найден.</returns>
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

        /// <summary>
        /// Удаляет файл сессии для указанного workflow.
        /// </summary>
        /// <param name="workflowFullPath">Полный путь к файлу workflow, сессию которого нужно сбросить.</param>
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