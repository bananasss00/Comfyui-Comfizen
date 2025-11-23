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
    # Make sure your IDE is configured to find this stub file (e.g., in .vscode/comfizen_api.pyi)
    from comfizen_api import ScriptContext

# Initialize 'ctx' to enable IntelliSense throughout the script.
# This variable is automatically replaced with the real context object at runtime.
ctx: ScriptContext = None


# --- .NET Assembly Imports ---
# No longer required for basic HTTP requests as they are now built into the ctx object.
# You may still need to import other .NET assemblies for advanced functionality.
# import clr
# clr.AddReference('System.Net.Http')


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
# --- Example 3: Making a POST Request (Simplified) ---
# ----------------------------------------------------------------------------------------
def example_post_request():
    """
    Demonstrates how to send a POST request with a JSON payload using the built-in ctx.post() method.
    This example posts data to httpbin.org, a free service for testing requests.
    """
    ctx.log("--- Running Example 3: POST Request (Simplified) ---")
    
    url = "https://httpbin.org/post"
    
    # Create a Python dictionary for your JSON data.
    # The `ctx.post` method will automatically serialize it to JSON.
    data_to_send = {
        "user": "Comfizen",
        "action": "test_post_simplified",
        "workflow_node_count": len(ctx.prompt)
    }
    
    try:
        ctx.log(f"Sending POST request to {url}...")
        
        # Call the built-in post method and synchronously wait for the result.
        response_content = ctx.post(url, data_to_send).Result
        
        if response_content:
            ctx.log("POST request successful. Server response from httpbin.org:")
            # Use print() for multi-line, formatted output
            print(response_content)
        else:
            ctx.log("POST request failed or returned no content.")
            
    except Exception as e:
        ctx.log(f"Error during POST request: {e}")


# ----------------------------------------------------------------------------------------
# --- Example 4: Making a GET Request (Simplified) ---
# ----------------------------------------------------------------------------------------
def example_get_request_reboot_server():
    """
    Demonstrates sending a GET request using the built-in ctx.get() method.
    This specific example triggers a server reboot via the ComfyUI-Manager custom node.
    
    Note: Requires ComfyUI-Manager to be installed.
    """
    ctx.log("--- Running Example 4: GET Request (Reboot Server) ---")
    
    url = f"http://{ctx.settings.ServerAddress}/manager/reboot"

    try:
        ctx.log(f"Sending GET request to {url} ...")
        
        # Call the built-in get method and synchronously wait for the result.
        response_text = ctx.get(url).Result
        
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
# --- Example 6: Applying Presets Programmatically (NEW) ---
# ----------------------------------------------------------------------------------------
def example_apply_presets():
    """
    Demonstrates how to apply global and local group presets from a script.
    This is powerful for automating complex UI state changes.
    """
    ctx.log("--- Running Example 6: Applying Presets ---")
    
    try:
        # Example 1: Apply a local preset to a specific group.
        # This will activate the "High Quality" preset within the "Sampler Settings" group.
        group_to_change = "Sampler Settings"
        preset_to_apply = "High Quality"
        ctx.log(f"Applying preset '{preset_to_apply}' to group '{group_to_change}'...")
        ctx.apply_group_preset(group_to_change, preset_to_apply)
        
        # Example 2: Apply a global preset.
        # This will activate a state across multiple groups as defined in the global preset.
        global_preset_name = "4K Upscale"
        ctx.log(f"Applying global preset '{global_preset_name}'...")
        ctx.apply_global_preset(global_preset_name)
        
        ctx.log("Preset application commands sent successfully.")
        
    except Exception as e:
        ctx.log(f"An error occurred while applying presets: {e}")


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
    # example_apply_presets()


# --- Execute the main function when the script is run ---
if __name__ == "__main__":
    main()