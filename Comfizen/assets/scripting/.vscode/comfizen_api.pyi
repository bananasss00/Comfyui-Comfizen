# File: .vscode/comfizen_api.pyi
# This is a type stub file to provide IntelliSense for the 'ctx' object
# injected by the Comfizen application into the IronPython script environment.

from typing import Any, Callable, Dict, List
from datetime import datetime

# --- Type Aliases and Helper Classes ---

# A placeholder for C# JObject. We treat it as a dictionary for autocompletion.
JObject = Dict[str, Any]

class _HttpContent:
    def ReadAsStringAsync(self) -> Any: ... # In scripts, use .Result to get the string

class _HttpResponseMessage:
    IsSuccessStatusCode: bool
    StatusCode: int
    Content: _HttpContent

class HttpClient:
    """
    A stub for the .NET System.Net.Http.HttpClient.
    Provides autocompletion for common methods.
    Note: These methods return a .NET Task. In scripts, use .Result to get the value synchronously.
    Example: response_text = ctx.http.GetStringAsync(url).Result
    """
    def GetStringAsync(self, url: str) -> Any: ...
    def PostAsync(self, url: str, content: Any) -> _HttpResponseMessage: ...
    def GetAsync(self, url: str) -> _HttpResponseMessage: ...
    def PutAsync(self, url: str, content: Any) -> _HttpResponseMessage: ...
    def DeleteAsync(self, url: str) -> _HttpResponseMessage: ...


# --- Main Class Definitions ---

class AppSettings:
    """Represents the application's user-configurable settings from settings.json."""
    SavedImagesDirectory: str
    Samplers: List[str]
    Schedulers: List[str]
    SessionsDirectory: str
    SavePromptWithFile: bool
    RemoveBase64OnSave: bool
    
    # Can be "Png", "Webp", or "Jpg"
    SaveFormat: str
    
    PngCompressionLevel: int
    WebpQuality: int
    JpgQuality: int
    MaxRecentWorkflows: int
    RecentWorkflows: List[str]
    MaxQueueSize: int
    ShowDeleteConfirmation: bool
    
    # Can be "Fixed", "Increment", "Decrement", "Randomize"
    LastSeedControlState: str
    
    ModelExtensions: List[str]
    LastOpenWorkflows: List[str]
    LastActiveWorkflow: str
    ServerAddress: str
    SpecialModelValues: List[str]
    Language: str

class ImageOutput:
    """
    Represents a single generated output (image or video), 
    available in the 'on_output_received' hook.
    """
    ImageBytes: bytes
    FileName: str
    Prompt: str
    CreatedAt: datetime
    VisualHash: str
    FilePath: str
    Resolution: str
    
    # Type is either "Image" or "Video"
    Type: str
    
    def GetHttpUri(self) -> str:
        """Returns a local HTTP URI for video playback."""
        ...

# --- The Main ScriptContext Class ---

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
    
    # An instance of the .NET HttpClient for making web requests.
    http: HttpClient
    
    # The function to log messages to the application's console.
    # Usage: ctx.log("My message")
    log: Callable[[str], None]
    
    # Provides access to all application settings.
    settings: AppSettings
    
    # Represents the generated file. Only available in the 'on_output_received' hook.
    # Will be None in other hooks or actions.
    output: ImageOutput