using System;
using System.IO;
using System.Threading.Tasks;
using ComfyUIConverter;

public class Program
{
    // Сделаем Main асинхронным
    public static async Task Main(string[] args)
    {
        // Перед запуском убедитесь, что ваш ComfyUI сервер запущен!
        string comfyUiServerUrl = "http://127.0.0.1:8188";
        
        try
        {
            Console.WriteLine($"Подключение к {comfyUiServerUrl} для получения информации о нодах...");
            
            // 1. Асинхронно создаем и инициализируем конвертер
            // Он сам сделает HTTP-запрос
            var converter = await ComfyUIWorkflowConverter.CreateAsync(comfyUiServerUrl);
            
            Console.WriteLine("Информация о нодах успешно получена. Начинаем конвертацию...");

            // 2. Загружаем JSON полного рабочего процесса
            string fullWorkflowJson = File.ReadAllText("default_full.json");
            
            // 3. Выполняем конвертацию (этот метод остался синхронным)
            string apiWorkflowJson = converter.ConvertWorkflow(fullWorkflowJson);

            // 4. Выводим и сохраняем результат
            Console.WriteLine("\n--- Результат в формате API ---");
            Console.WriteLine(apiWorkflowJson);

            File.WriteAllText("converted_api_auto.json", apiWorkflowJson);
            Console.WriteLine("\nРезультат сохранен в файл 'converted_api_auto.json'");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nПРОИЗОШЛА КРИТИЧЕСКАЯ ОШИБКА:");
            Console.WriteLine(ex.Message);
            if(ex.InnerException != null)
            {
                Console.WriteLine($"\nПодробности: {ex.InnerException.Message}");
            }
            Console.WriteLine("\nУбедитесь, что ComfyUI запущен и доступен по указанному адресу.");
            Console.ResetColor();
        }
    }
}