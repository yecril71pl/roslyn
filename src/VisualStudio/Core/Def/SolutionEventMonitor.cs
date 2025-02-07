﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.VisualStudio.LanguageServices
{
    /// <summary>
    /// Monitors Visual Studio's UIContext for Solution building/opening/closing and notifies the GlobalOperationService.
    /// The intent is to suspend analysis of non-essential files for the duration of these solution operations.
    /// </summary>
    internal sealed class SolutionEventMonitor : IDisposable
    {
        private const string SolutionBuilding = "Solution Building";
        private const string SolutionOpening = "Solution Opening";
        private const string SolutionClosing = "Solution Closing";

        private readonly UIContext? _solutionClosingContext;
        private IGlobalOperationNotificationService? _notificationService;
        private readonly Dictionary<string, GlobalOperationRegistration> _operations = new();

        public SolutionEventMonitor(VisualStudioWorkspace workspace)
        {
            if (workspace.Services.GetService<IGlobalOperationNotificationService>() is GlobalOperationNotificationService notificationService)
            {
                // subscribe to events only if it is normal service. if it is one from unit test or other, don't bother to subscribe
                _notificationService = notificationService;

                // make sure we set initial state correctly. otherwise, we can get into a race where we might miss the very first events
                if (KnownUIContexts.SolutionBuildingContext.IsActive)
                {
                    ContextChanged(active: true, operation: SolutionBuilding);
                }

                KnownUIContexts.SolutionBuildingContext.UIContextChanged += SolutionBuildingContextChanged;

                // make sure we set initial state correctly. otherwise, we can get into a race where we might miss the very first events
                if (KnownUIContexts.SolutionOpeningContext.IsActive)
                {
                    ContextChanged(active: true, operation: SolutionOpening);
                }

                KnownUIContexts.SolutionOpeningContext.UIContextChanged += SolutionOpeningContextChanged;

                _solutionClosingContext = UIContext.FromUIContextGuid(VSConstants.UICONTEXT.SolutionClosing_guid);

                // make sure we set initial state correctly. otherwise, we can get into a race where we might miss the very first events
                if (_solutionClosingContext.IsActive)
                {
                    ContextChanged(active: true, operation: SolutionClosing);
                }

                _solutionClosingContext.UIContextChanged += SolutionClosingContextChanged;
            }
        }

        public void Dispose()
        {
            foreach (var globalOperation in _operations.Values)
            {
                globalOperation.Dispose();
            }

            _operations.Clear();

            if (_notificationService != null)
            {
                _notificationService = null;
                KnownUIContexts.SolutionBuildingContext.UIContextChanged -= SolutionBuildingContextChanged;
                KnownUIContexts.SolutionOpeningContext.UIContextChanged -= SolutionOpeningContextChanged;

                if (_solutionClosingContext != null)
                    _solutionClosingContext.UIContextChanged -= SolutionClosingContextChanged;
            }
        }

        private void SolutionBuildingContextChanged(object sender, UIContextChangedEventArgs e)
            => ContextChanged(e.Activated, SolutionBuilding);

        private void SolutionOpeningContextChanged(object sender, UIContextChangedEventArgs e)
            => ContextChanged(e.Activated, SolutionOpening);

        private void SolutionClosingContextChanged(object sender, UIContextChangedEventArgs e)
            => ContextChanged(e.Activated, SolutionClosing);

        private void ContextChanged(bool active, string operation)
        {
            if (_notificationService == null)
            {
                return;
            }

            TryCancelPendingNotification(operation);

            if (active)
            {
                _operations[operation] = _notificationService.Start(operation);
            }
        }

        private void TryCancelPendingNotification(string operation)
        {
            if (_operations.TryGetValue(operation, out var globalOperation))
            {
                globalOperation.Done();
                globalOperation.Dispose();

                _operations.Remove(operation);
            }
        }
    }
}
