import sys
import server
import logging

print("--- [Comfyui-ConsoleBridgeNode] Initializing full console stream hijack ---")

# Получаем экземпляр PromptServer для отправки сообщений по WebSocket
prompt_server = server.PromptServer.instance

# --- 1. Обработчик для модуля logging ---

class WebSocketLoggingHandler(logging.Handler):
    def emit(self, record):
        """
        Этот метод вызывается для каждой записи лога (info, warning, error и т.д.).
        """
        try:
            # Форматируем сообщение лога в строку
            message = self.format(record)
            
            if message.strip():
                prompt_server.send_sync("console_log_message", {
                    "level": record.levelname,
                    "message": message
                })
        except Exception as e:
            # В случае ошибки выводим ее в оригинальный stderr
            original_stderr.write(f"[WebSocketLogStreamer] Logging Handler Error: {e}\n")
            original_stderr.flush()

# --- 2. Перехватчик для прямого вывода в stderr (для tqdm и др.) ---

# Сохраняем оригинальный stderr на случай, если что-то пойдет не так
original_stderr = sys.stderr

class WebSocketStderrWriter:
    def write(self, text):
        # Отправляем текст напрямую по WebSocket
        if text.strip():
            try:
                # Используем другой тип сообщения, чтобы клиент мог их различать
                prompt_server.send_sync("console_stderr_output", {
                    "text": text
                })
            except Exception as e:
                original_stderr.write(f"[WebSocketLogStreamer] Stderr Writer Error: {e}\n")

        # Дублируем вывод в оригинальную консоль
        original_stderr.write(text)
        original_stderr.flush()

    def flush(self):
        original_stderr.flush()

# --- Применение перехватчиков ---

# Создаем и добавляем наш обработчик логов в корневой логгер.
# Теперь все вызовы logging.info(), logging.error() и т.д. будут проходить через него.
handler = WebSocketLoggingHandler()
# Устанавливаем простой формат, чтобы не было дублирования информации
formatter = logging.Formatter('%(message)s')
handler.setFormatter(formatter)
logging.getLogger().addHandler(handler)

# Перехватываем только stderr для tqdm и других прямых записей.
# stdout больше не трогаем, так как logging теперь обрабатывается напрямую.
sys.stderr = WebSocketStderrWriter()

print("--- [Comfyui-ConsoleBridgeNode] Full console stream hijack is active ---")

# Стандартная часть для любого кастомного узла
NODE_CLASS_MAPPINGS = {}
NODE_DISPLAY_NAME_MAPPINGS = {}