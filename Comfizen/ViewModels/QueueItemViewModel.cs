using System;
using System.Collections.ObjectModel; // Add this
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.ComponentModel;

namespace Comfizen
{
    /// <summary>
    /// Represents a single task in the pending queue UI.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class QueueItemViewModel : INotifyPropertyChanged
    {
        public Guid Id { get; } = Guid.NewGuid();
        public MainViewModel.PromptTask Task { get; }
        public string WorkflowName { get; }

        /// <summary>
        /// A snapshot of the workflow's API state at the moment of queuing. Used for comparison.
        /// </summary>
        public JObject TemplatePrompt { get; }
        
        // START OF CHANGE: Add collection for details
        public ObservableCollection<QueueItemDetailViewModel> Details { get; } = new ObservableCollection<QueueItemDetailViewModel>();
        // END OF CHANGE

        public event PropertyChangedEventHandler PropertyChanged;

        public QueueItemViewModel(MainViewModel.PromptTask task, string workflowName, JObject templatePrompt)
        {
            Task = task;
            WorkflowName = workflowName;
            TemplatePrompt = templatePrompt;
        }
    }
}