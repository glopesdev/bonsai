﻿using System;
using System.IO;
using System.Windows.Forms;
using Bonsai.Editor.GraphModel;
using Bonsai.Expressions;

namespace Bonsai.Editor
{
    interface IWorkflowEditorService
    {
        string GetProjectDisplayName();

        void OnKeyDown(KeyEventArgs e);

        void OnKeyPress(KeyPressEventArgs e);

        void OnContextMenuOpening(EventArgs e);

        void OnContextMenuClosed(EventArgs e);

        DirectoryInfo EnsureExtensionsDirectory();

        WorkflowBuilder LoadWorkflow(string fileName);

        void NavigateTo(WorkflowEditorPath workflowPath, NavigationPreference navigationPreference = default);

        void SelectBuilderNode(ExpressionBuilder builder, NavigationPreference navigationPreference = default);

        void SelectBuilderNode(WorkflowEditorPath builderPath, NavigationPreference navigationPreference = default);

        void SelectNextControl(bool forward);

        bool ValidateWorkflow();

        void RefreshEditor();
    }
}
