import sys
import server
import logging
from types import TracebackType
from typing import Optional, Type

print("--- [ComfyUI-ConsoleBridge] Initializing console stream hijack ---")

# Get the PromptServer instance to send WebSocket messages
prompt_server = server.PromptServer.instance

# --- 1. Handler for the logging module ---

class WebSocketLoggingHandler(logging.Handler):
    """
    This logging handler intercepts log records (info, warning, error, etc.)
    and sends them over the WebSocket connection.
    """
    def emit(self, record: logging.LogRecord):
        try:
            message = self.format(record)
            if message.strip():
                prompt_server.send_sync("console_log_message", {
                    "level": record.levelname,
                    "message": message
                })
        except Exception as e:
            # On error, write to the original stderr to avoid an infinite loop
            original_stderr.write(f"[WebSocketStreamer] Logging Handler Error: {e}\n")
            original_stderr.flush()

# --- 2. Generic stream redirector for stdout and stderr ---

# Save the original streams before they are replaced
original_stdout = sys.stdout
original_stderr = sys.stderr

class WebSocketStreamRedirector:
    """
    A wrapper class to intercept writes to a stream (like stdout or stderr)
    and forward them over a WebSocket. It also delegates any other attribute
    access (e.g., isatty) to the original stream to ensure compatibility.
    """
    def __init__(self, original_stream, message_type: str):
        self.original_stream = original_stream
        self.message_type = message_type

    def write(self, text: str):
        # Send the text over the WebSocket if it's not empty
        if text.strip():
            try:
                prompt_server.send_sync(self.message_type, {"text": text})
            except Exception as e:
                # Use a specific error message and the original stderr
                error_prefix = f"[WebSocketStreamer] {self.message_type} Error"
                original_stderr.write(f"{error_prefix}: {e}\n")

        # Duplicate the output to the original console
        self.original_stream.write(text)
        self.original_stream.flush()

    def flush(self):
        self.original_stream.flush()

    def __getattr__(self, name: str):
        """
        Redirects all other attribute requests (e.g., isatty, encoding)
        to the original stream object to maintain its behavior.
        """
        return getattr(self.original_stream, name)

# --- 3. Applying the hijack ---

# Set up and add the logging handler to the root logger.
# All calls to logging.info(), logging.error(), etc., will now be intercepted.
handler = WebSocketLoggingHandler()
formatter = logging.Formatter('%(message)s')
handler.setFormatter(formatter)
logging.getLogger().addHandler(handler)
# Optionally set the root logger level if you want to capture everything
# logging.getLogger().setLevel(logging.INFO)

# Hijack stdout and stderr using our generic redirector class.
sys.stdout = WebSocketStreamRedirector(original_stdout, "console_stdout_output")
sys.stderr = WebSocketStreamRedirector(original_stderr, "console_stderr_output")

print("--- [ComfyUI-ConsoleBridge] Console stream hijack is active ---")

# Standard boilerplate for custom nodes
NODE_CLASS_MAPPINGS = {}
NODE_DISPLAY_NAME_MAPPINGS = {}