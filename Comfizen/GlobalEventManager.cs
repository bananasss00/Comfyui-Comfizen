using System;

namespace Comfizen
{
    /// <summary>
    /// Определяет тип изменения, произошедшего при сохранении воркфлоу.
    /// </summary>
    public enum WorkflowSaveType
    {
        /// <summary>
        /// Изменилась только разметка UI (группы, поля, их типы). Текущие значения виджетов должны быть сохранены.
        /// </summary>
        LayoutOnly,
        /// <summary>
        /// Был загружен новый базовый API-файл. Текущие значения виджетов несовместимы и должны быть сброшены.
        /// </summary>
        ApiReplaced
    }

    /// <summary>
    /// Аргументы для события сохранения воркфлоу.
    /// </summary>
    public class WorkflowSaveEventArgs : EventArgs
    {
        public string FilePath { get; }
        public WorkflowSaveType SaveType { get; }

        public WorkflowSaveEventArgs(string filePath, WorkflowSaveType saveType)
        {
            FilePath = filePath;
            SaveType = saveType;
        }
    }

    /// <summary>
    /// Глобальный менеджер событий для связи между окнами.
    /// </summary>
    public static class GlobalEventManager
    {
        public static event EventHandler<WorkflowSaveEventArgs> WorkflowSaved;

        public static void RaiseWorkflowSaved(string filePath, WorkflowSaveType saveType)
        {
            WorkflowSaved?.Invoke(null, new WorkflowSaveEventArgs(filePath, saveType));
        }
    }
}