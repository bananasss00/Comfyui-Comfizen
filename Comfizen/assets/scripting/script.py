##########################################################################################
#                                                                                        #
#                    Comfizen Scripting Examples & Best Practices                        #
#                                                                                        #
#  This file demonstrates various ways to interact with the Comfizen application,        #
#  the ComfyUI workflow (prompt), and external APIs using the 'ctx' object.              #
#                                                                                        #
#  - To use an example, uncomment its function call in the main() function at the end.   #
#  - It's recommended to have only one function call active at a time for clarity.       #
#                                                                                        #
##########################################################################################


# --- IntelliSense & Type Checking Block ---
# This block is only for static analysis (e.g., in VS Code) and is ignored by
# the IronPython runtime engine. It enables autocompletion and type checking.
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from comfizen_api import ScriptContext

# Initialize 'ctx' to enable IntelliSense throughout the script.
# This variable is automatically replaced with the real context object at runtime.
ctx: ScriptContext = None


# --- .NET Assembly Imports ---
# For advanced operations like HTTP requests, you may need to load .NET assemblies.
import clr
clr.AddReference('System.Net.Http')

from System.Text import Encoding
from System.Net.Http import StringContent


# ----------------------------------------------------------------------------------------
# --- Example 1: Reading and Modifying the Current Workflow (Prompt) ---
# ----------------------------------------------------------------------------------------
def example_read_and_write_prompt():
    """
    Demonstrates how to read a value from a node in the workflow,
    log it, modify it, and log the new value. This is the most common use case.
    
    Setup: This example assumes your workflow has a node with the ID "5" and that
           this node has an input named "seed".
    """
    ctx.log("--- Running Example 1: Read and Write Prompt ---")
    
    try:
        # Safely access nested values in the prompt. Using .get() with a default
        # value (like an empty dictionary {}) prevents errors if a key doesn't exist.
        node_id = "5"
        seed_node = ctx.prompt.get(node_id, {})
        seed_value = seed_node.get("inputs", {}).get("seed")

        if seed_value is None:
            ctx.log(f"Warning: Could not find 'seed' input in node '{node_id}'.")
            return

        ctx.log(f"Read seed from Node {node_id}: {seed_value}")

        # Modify the value. This change will be applied to the prompt that gets queued.
        new_seed = int(seed_value) + 1
        ctx.prompt[node_id]["inputs"]["seed"] = new_seed

        ctx.log(f"Set new seed for Node {node_id}: {new_seed}")

    except Exception as e:
        ctx.log(f"An error occurred in Example 1: {e}")


# ----------------------------------------------------------------------------------------
# --- Example 2: Using Persistent State ---
# ----------------------------------------------------------------------------------------
def example_persistent_counter():
    """
    Demonstrates the use of `ctx.state`, a dictionary that persists between
    script executions. This allows you to store data across multiple button clicks.
    """
    ctx.log("--- Running Example 2: Persistent Counter ---")

    # Safely get the counter value, defaulting to 0 on the first run.
    counter = ctx.state.get("click_counter", 0)
    counter += 1

    # Store the updated value back into the state dictionary.
    ctx.state["click_counter"] = counter

    ctx.log(f"This button has been clicked {counter} time(s).")


# ----------------------------------------------------------------------------------------
# --- Example 3: Making a POST Request with a JSON Payload ---
# ----------------------------------------------------------------------------------------
def example_post_request():
    """
    Demonstrates how to send a POST request with a JSON payload to an external API.
    This example posts data to httpbin.org, a free service for testing requests.
    """
    ctx.log("--- Running Example 3: POST Request ---")
    
    url = "https://httpbin.org/post"
    
    # Create a Python dictionary for your JSON data.
    data_to_send = {
        "user": "Comfizen",
        "action": "test_post",
        "workflow_node_count": len(ctx.prompt)
    }
    
    # Convert the Python dictionary to a JSON string.
    # Note: IronPython 2.7 doesn't have a built-in 'json' module.
    # We use Newtonsoft.Json, which is available in the host application.
    # A simple string-based approach is often easier for simple JSON.
    payload_string = "{"
    payload_string += f"\"user\": \"{data_to_send['user']}\", "
    payload_string += f"\"action\": \"{data_to_send['action']}\", "
    payload_string += f"\"workflow_node_count\": {data_to_send['workflow_node_count']}"
    payload_string += "}"

    try:
        ctx.log(f"Sending POST request to {url} with payload: {payload_string}")
        
        # Create StringContent with the correct encoding and content type.
        content = StringContent(payload_string, Encoding.UTF8, "application/json")
        
        # Send the POST request and wait for the result synchronously.
        response = ctx.http.PostAsync(url, content).Result
        
        # Read the response content as a string.
        response_content = response.Content.ReadAsStringAsync().Result
        
        ctx.log(f"POST request completed with status: {response.StatusCode}")
        
        if response.IsSuccessStatusCode:
            ctx.log("Server response from httpbin.org:")
            print(response_content) # Use print() for multi-line, formatted output
        else:
            ctx.log(f"Server returned an error: {response_content}")
            
    except Exception as e:
        ctx.log(f"Error during POST request: {e}")


# ----------------------------------------------------------------------------------------
# --- Example 4: Making a GET Request (ComfyUI Manager Reboot) ---
# ----------------------------------------------------------------------------------------
def example_get_request_reboot_server():
    """
    Demonstrates sending a GET request to a local ComfyUI API endpoint.
    This specific example triggers a server reboot via the ComfyUI-Manager custom node.
    
    Note: Requires ComfyUI-Manager to be installed.
    """
    ctx.log("--- Running Example 4: GET Request (Reboot Server) ---")
    
    url = f"http://{ctx.settings.ServerAddress}/manager/reboot"

    try:
        ctx.log(f"Sending GET request to {url} ...")
        
        # GetStringAsync is a simple way to make a GET request and read the response.
        response_text = ctx.http.GetStringAsync(url).Result
        
        ctx.log("GET request successful. Server response:")
        print(response_text)
        
    except Exception as e:
        # This will often throw an error because the server disconnects during reboot, which is expected.
        ctx.log(f"Request sent. The server is rebooting. Any connection error here is expected: {e}")


# ----------------------------------------------------------------------------------------
# --- Example 5: Reading Application Settings ---
# ----------------------------------------------------------------------------------------
def example_read_settings():
    """
    Demonstrates how to access the application's settings from the script.
    This can be useful for creating scripts that adapt to the user's configuration.
    """
    ctx.log("--- Running Example 5: Read Settings ---")
    
    server_address = ctx.settings.ServerAddress
    save_dir = ctx.settings.SavedImagesDirectory
    save_format = ctx.settings.SaveFormat
    
    ctx.log(f"Current server address: {server_address}")
    ctx.log(f"Default image save directory: {save_dir}")
    ctx.log(f"Default save format: {save_format}")


# ----------------------------------------------------------------------------------------
# --- Main Entry Point ---
# ----------------------------------------------------------------------------------------
def main():
    """
    Main function to run the selected example.
    Uncomment the example you wish to execute.
    """
    
    # --- UNCOMMENT ONE OF THE FOLLOWING LINES TO RUN AN EXAMPLE ---
    
    example_read_and_write_prompt()
    # example_persistent_counter()
    # example_post_request()
    # example_get_request_reboot_server()
    # example_read_settings()


# --- Execute the main function when the script is run ---
if __name__ == "__main__":
    main()