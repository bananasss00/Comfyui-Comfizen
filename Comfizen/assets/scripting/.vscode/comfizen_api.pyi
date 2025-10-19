# File: .vscode/comfizen_api.pyi
# This is an updated type stub file to provide IntelliSense for the 'ctx' object
# injected by the Comfizen application into the IronPython script environment.

from typing import Any, Callable, Dict, List, Optional
from datetime import datetime

# --- Type Aliases and Helper Classes ---

# A placeholder for C# JObject. We treat it as a dictionary for autocompletion.
JObject = Dict[str, Any]

# --- Main Class Definitions ---

class AppSettings:
    """Represents the application's user-configurable settings from settings.json."""
    ServerAddress: str
    SessionsDirectory: str
    SavedImagesDirectory: str
    SavePromptWithFile: bool
    RemoveBase64OnSave: bool
    SaveFormat: str  # Can be "Png", "Webp", or "Jpg"
    PngCompressionLevel: int
    WebpQuality: int
    JpgQuality: int
    MaxQueueSize: int
    ShowDeleteConfirmation: bool
    LastSeedControlState: str  # "Fixed", "Increment", "Decrement", "Randomize"
    MaxRecentWorkflows: int
    RecentWorkflows: List[str]
    LastOpenWorkflows: List[str]
    LastActiveWorkflow: str
    ModelExtensions: List[str]
    Samplers: List[str]
    Schedulers: List[str]
    SpecialModelValues: List[str]
    Language: str

class ImageOutput:
    """
    Represents a single generated output (image or video), 
    available in the 'on_output_received' hook.
    """
    ImageBytes: bytes
    FileName: str
    FilePath: str
    Prompt: str
    CreatedAt: datetime
    Resolution: str
    Type: str  # "Image" or "Video"
    VisualHash: str
    
    def GetHttpUri(self) -> str:
        """Returns a local HTTP URI for video playback."""
        ...

# --- The Main ScriptContext Class (Reflects the actual C# API) ---

class ScriptContext:
    """
    Provides context and helper functions to the Python script at runtime.
    This is the main 'ctx' object.
    """
    
    # Represents the entire workflow JSON object.
    # You can access nodes like ctx.prompt["12"]["inputs"]["seed"]
    prompt: JObject
    
    # A persistent dictionary to store state between script executions.
    state: Dict[str, Any]
    
    # The function to log messages to the application's console.
    # Usage: ctx.log("My message")
    log: Callable[[str], None]
    
    # Provides access to all application settings.
    settings: AppSettings
    
    # Represents the generated file. Only available in the 'on_output_received' hook.
    # Will be None in other hooks or actions.
    output: Optional[ImageOutput]

    def get(self, url: str) -> Optional[str]:
        """
        Executes an HTTP GET request.
        Note: This method is asynchronous. In IronPython, you must call .Result to get the value.
        Example: html_content = ctx.get("https://google.com").Result
        """
        ...

    def post(self, url: str, data: Any) -> Optional[str]:
        """
        Executes an HTTP POST request.
        Note: This method is asynchronous. In IronPython, you must call .Result to get the value.
        Example: response = ctx.post("http://api/data", {"key": "value"}).Result
        """
        ...

# --- Global variable available in the script ---
ctx: ScriptContext