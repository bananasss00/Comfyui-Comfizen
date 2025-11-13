using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Comfizen
{
    /// <summary>
    /// A dedicated service to handle the logic of bypassing nodes in a ComfyUI workflow prompt.
    /// This class is stateless and performs its operations on the JObject prompt provided to it.
    /// </summary>
    public class NodeBypassService
    {
        private readonly JObject _objectInfo;
        private readonly IReadOnlyDictionary<string, JObject> _nodeConnectionSnapshots;
        private readonly IEnumerable<WorkflowUITabLayoutViewModel> _tabLayouts;

        /// <summary>
        /// Initializes a new instance of the NodeBypassService.
        /// </summary>
        /// <param name="objectInfo">The pre-loaded object_info from the ComfyUI API.</param>
        /// <param name="nodeConnectionSnapshots">A dictionary containing the original connections of nodes.</param>
        /// <param name="tabLayouts">The collection of UI tab layouts to scan for bypass controls.</param>
        public NodeBypassService(JObject objectInfo, IReadOnlyDictionary<string, JObject> nodeConnectionSnapshots, IEnumerable<WorkflowUITabLayoutViewModel> tabLayouts)
        {
            _objectInfo = objectInfo;
            _nodeConnectionSnapshots = nodeConnectionSnapshots;
            _tabLayouts = tabLayouts;
        }
        
        /// <summary>
        /// Modifies a JObject prompt in-place to apply node bypass logic for a single generation run.
        /// This method orchestrates the bypass process by restoring original connections,
        /// identifying active bypasses, and then rerouting the connection graph to skip the bypassed nodes.
        /// </summary>
        /// <param name="prompt">The JObject representing the workflow API, which will be modified directly.</param>
        public void ApplyBypass(JObject prompt)
        {
            // Step 1: Always restore the prompt to its original state from our snapshots.
            RestoreOriginalNodeConnections(prompt);

            // Step 2: Find all the UI controls responsible for bypassing nodes.
            var bypassViewModels = FindAllBypassViewModels();
            if (!bypassViewModels.Any())
            {
                return; // No bypass controls exist in the UI definition.
            }

            // Step 3: From the UI controls, determine which nodes need to be bypassed for this specific run.
            var nodesToBypass = GetActiveBypassNodeIds(bypassViewModels);
            if (!nodesToBypass.Any())
            {
                return; // No bypasses are currently enabled by the user.
            }

            // Step 4: Disconnect the inputs OF the bypassed nodes to prevent their execution.
            DisconnectInputsOfBypassedNodes(prompt, nodesToBypass);

            // Step 5: Build a map that tells us how to reroute connections around the bypassed nodes.
            var redirectionMap = BuildRedirectionMap(prompt, nodesToBypass);

            // Step 6: Go through the prompt and rewire the inputs of all downstream nodes.
            RewireDownstreamConnections(prompt, nodesToBypass, redirectionMap);
        }

        /// <summary>
        /// Restores all node input connections from the stored snapshots.
        /// </summary>
        public void RestoreOriginalNodeConnections(JObject prompt)
        {
            foreach (var snapshot in _nodeConnectionSnapshots)
            {
                var nodeId = snapshot.Key;
                var originalConnections = snapshot.Value;

                if (prompt[nodeId] is JObject node && node["inputs"] is JObject currentInputs)
                {
                    currentInputs.Merge(originalConnections.DeepClone(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                }
            }
        }

        /// <summary>
        /// Scans the UI layout to find all NodeBypassFieldViewModel instances.
        /// </summary>
        private List<NodeBypassFieldViewModel> FindAllBypassViewModels()
        {
            return _tabLayouts
                .SelectMany(t => t.Groups)
                .SelectMany(g => g.Tabs.SelectMany(tab => tab.Fields))
                .OfType<NodeBypassFieldViewModel>()
                .ToList();
        }

        /// <summary>
        /// Gathers a set of all unique node IDs that should be bypassed based on the UI state.
        /// </summary>
        private HashSet<string> GetActiveBypassNodeIds(IEnumerable<NodeBypassFieldViewModel> bypassViewModels)
        {
            var nodesToBypass = new HashSet<string>();
            foreach (var vm in bypassViewModels.Where(vm => !vm.IsEnabled)) // Bypass is active when checkbox is UNCHECKED
            {
                foreach (var nodeId in vm.BypassNodeIds)
                {
                    nodesToBypass.Add(nodeId);
                }
            }
            return nodesToBypass;
        }

        /// <summary>
        /// Prevents bypassed nodes from executing by removing their input connections.
        /// </summary>
        private void DisconnectInputsOfBypassedNodes(JObject prompt, IReadOnlySet<string> nodesToBypass)
        {
            foreach (var bypassedNodeId in nodesToBypass)
            {
                if (prompt[bypassedNodeId]?["inputs"] is not JObject bypassedNodeInputs) continue;

                var connectionProperties = bypassedNodeInputs.Properties()
                    .Where(p => p.Value is JArray)
                    .ToList();
                
                foreach (var prop in connectionProperties)
                {
                    prop.Remove();
                }
            }
        }

        /// <summary>
        /// Creates a mapping to redirect connections around bypassed nodes.
        /// </summary>
        private Dictionary<string, JArray> BuildRedirectionMap(JObject prompt, IReadOnlySet<string> nodesToBypass)
        {
            var redirectionMap = new Dictionary<string, JArray>();
            foreach (var bypassedNodeId in nodesToBypass)
            {
                if (!_nodeConnectionSnapshots.TryGetValue(bypassedNodeId, out var originalInputs))
                {
                    continue;
                }

                string classType = prompt[bypassedNodeId]?["class_type"]?.ToString();
                if (string.IsNullOrEmpty(classType) || _objectInfo?[classType] == null) continue;
                
                var nodeInfo = _objectInfo[classType];
                var outputTypes = nodeInfo["output"]?.ToObject<List<string>>();
                var inputDefs = nodeInfo["input"]?["required"]?.ToObject<JObject>();

                if (outputTypes == null || inputDefs == null) continue;
                
                foreach (var inputProp in originalInputs.Properties().Where(p => p.Value is JArray))
                {
                    if (inputProp.Value is not JArray sourceLink) continue;
                    if (!inputDefs.TryGetValue(inputProp.Name, out var inputDefToken) || inputDefToken is not JArray inputDefArray) continue;
                    
                    string inputType = inputDefArray[0].ToString();
                    int outputIndex = outputTypes.IndexOf(inputType);

                    if (outputIndex != -1)
                    {
                        string outputKey = $"{bypassedNodeId}.{outputIndex}";
                        redirectionMap[outputKey] = sourceLink;
                    }
                }
            }
            return redirectionMap;
        }

        /// <summary>
        /// Iterates through the prompt and updates input connections to skip bypassed nodes.
        /// </summary>
        private void RewireDownstreamConnections(JObject prompt, IReadOnlySet<string> nodesToBypass, IReadOnlyDictionary<string, JArray> redirectionMap)
        {
            foreach (var nodeProperty in prompt.Properties())
            {
                if (nodeProperty.Value is not JObject node || node["inputs"] is not JObject nodeInputs) continue;
                
                var inputsToRemove = new List<JProperty>();
                
                foreach (var inputProperty in nodeInputs.Properties())
                {
                    if (inputProperty.Value is not JArray originalLink || originalLink.Count != 2) continue;

                    JArray currentLink = originalLink;
                    int depth = 0;
                    const int maxDepth = 20;

                    while (depth < maxDepth)
                    {
                        string sourceNodeId = currentLink[0].ToString();
                        if (!nodesToBypass.Contains(sourceNodeId)) break;

                        string sourceOutputIndex = currentLink[1].ToString();
                        string sourceKey = $"{sourceNodeId}.{sourceOutputIndex}";
                        
                        if (redirectionMap.TryGetValue(sourceKey, out var newSourceLink))
                        {
                            currentLink = newSourceLink; 
                        }
                        else
                        {
                            break;
                        }
                        depth++;
                    }
                    
                    string finalSourceNodeId = currentLink[0].ToString();
                    if (nodesToBypass.Contains(finalSourceNodeId))
                    {
                        inputsToRemove.Add(inputProperty);
                    }
                    else if (currentLink != originalLink)
                    {
                        inputProperty.Value = currentLink.DeepClone();
                    }
                }
                
                foreach (var propToRemove in inputsToRemove)
                {
                    propToRemove.Remove();
                }
            }
        }
    }
}