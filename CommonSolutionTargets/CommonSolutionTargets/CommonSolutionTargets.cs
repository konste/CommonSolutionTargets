//------------------------------------------------------------------------------
// <copyright file="CommonSolutionTargets.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CommonSolutionTargets
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [Guid(CommonSolutionTargets.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideAutoLoad(UIContextGuids.SolutionExists)]
    public sealed class CommonSolutionTargets : Package, IVsUpdateSolutionEvents
    {
        /// <summary>
        /// CommonSolutionTargets GUID string.
        /// </summary>
        public const string PackageGuidString = "b49988b3-37f1-4195-b984-c64b4885432c";

        private DTE2 dte2;
        private IVsSolutionBuildManager2 solutionBuildManager;
        private uint updateSolutionEventsCookie;
        IVsOutputWindowPane buildOutputWindowPane;

        private CommandEvents commandEvents;
        private string VSStd97CmdIDGuid;
        private string VSStd2KCmdIDGuid;
        private string activeBuildTarget;

        private LoggerVerbosity currentLoggerVerbosity = LoggerVerbosity.Minimal;

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public CommonSolutionTargets()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this));
        }



        /////////////////////////////////////////////////////////////////////////////
        // Overridden Package Implementation
        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this));
            base.Initialize();

            this.dte2 = this.GetService(typeof(SDTE)) as DTE2;
            if (this.dte2 == null)
            {
                Log.LogError("VSPackage.Initialize() could not obtain DTE2 reference");
                return;
            }

            this.RefreshMSBuildOutputVerbositySetting();

            // Get solution build manager
            this.solutionBuildManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager2;
            if (this.solutionBuildManager != null)
            {
                this.solutionBuildManager.AdviseUpdateSolutionEvents(this, out this.updateSolutionEventsCookie);
            }

            IVsOutputWindow outputWindow = this.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
            {
                Log.LogError("VSPackage.Initialize() could not obtain IVsOutputWindow reference");
                return;
            }

            Guid buildPaneGuid = VSConstants.GUID_BuildOutputWindowPane;
            int hResult = outputWindow.GetPane(ref buildPaneGuid, out this.buildOutputWindowPane);
            if (hResult != VSConstants.S_OK || this.buildOutputWindowPane == null)
            {
                Log.LogError("VSPackage.Initialize() could not obtain IVsOutputWindowPane reference");
                return;
            }

            GuidAttribute VSStd97CmdIDGuidAttribute = typeof(VSConstants.VSStd97CmdID).GetCustomAttributes(typeof(GuidAttribute), true)[0] as GuidAttribute;
            Debug.Assert(VSStd97CmdIDGuidAttribute != null, "VSStd97CmdIDGuidAttribute != null");
            this.VSStd97CmdIDGuid = "{" + VSStd97CmdIDGuidAttribute.Value + "}";

            GuidAttribute VSStd2KCmdIDGuidAttribute = typeof(VSConstants.VSStd2KCmdID).GetCustomAttributes(typeof(GuidAttribute), true)[0] as GuidAttribute;
            Debug.Assert(VSStd2KCmdIDGuidAttribute != null, "VSStd2KCmdIDGuidAttribute != null");
            this.VSStd2KCmdIDGuid = "{" + VSStd2KCmdIDGuidAttribute.Value + "}";

            this.commandEvents = this.dte2.Events.CommandEvents;
            this.commandEvents.BeforeExecute += this.CommandEvents_BeforeExecute;

        }

        void CommandEvents_BeforeExecute(string Guid, int ID, object CustomIn, object CustomOut, ref bool CancelDefault)
        {
            if (!Guid.Equals(this.VSStd97CmdIDGuid, StringComparison.OrdinalIgnoreCase) &&
                !Guid.Equals(this.VSStd2KCmdIDGuid, StringComparison.OrdinalIgnoreCase))
                return;

            //Debug.WriteLine("ID= " + ID);
            switch (ID)
            {
                case (int)BuildType.Build:
                case (int)BuildType.BuildSelection:
                case (int)BuildType.BuildOnlyProject:
                case (int)BuildType.BuildCtx:
                    this.activeBuildTarget = "Build";
                    break;
                case (int)BuildType.Rebuild:
                case (int)BuildType.RebuildSelection:
                case (int)BuildType.RebuildOnlyProject:
                case (int)BuildType.RebuildCtx:
                    this.activeBuildTarget = "Rebuild";
                    break;
                case (int)BuildType.Clean:
                case (int)BuildType.CleanSelection:
                case (int)BuildType.CleanOnlyProject:
                case (int)BuildType.CleanCtx:
                    this.activeBuildTarget = "Clean";
                    break;
                case (int)BuildType.Deploy:
                case (int)BuildType.DeploySelection:
                case (int)BuildType.DeployCtx:
                    this.activeBuildTarget = "Deploy";
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Unadvise all events
            if (this.solutionBuildManager != null && this.updateSolutionEventsCookie != 0)
                this.solutionBuildManager.UnadviseUpdateSolutionEvents(this.updateSolutionEventsCookie);
        }
        #endregion

        #region Implementation of IVsUpdateSolutionEvents

        /// <summary>
        /// Called before any build actions have begun. This is the last chance to cancel the build before any building begins.
        /// </summary>
        /// <returns>
        /// If the method succeeds, it returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"/>. If it fails, it returns an error code.
        /// </returns>
        /// <param name="pfCancelUpdate">[in, out] Pointer to a flag indicating cancel update.</param>
        int IVsUpdateSolutionEvents.UpdateSolution_Begin(ref int pfCancelUpdate)
        {
            this.WriteLine(LoggerVerbosity.Diagnostic, "CommonSolutionTargets UpdateSolution_Begin");
            return 0;
        }

        /// <summary>
        /// Called when a build is completed.
        /// </summary>
        /// <returns>
        /// If the method succeeds, it returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"/>. If it fails, it returns an error code.
        /// </returns>
        /// <param name="fSucceeded">[in] true if no update actions failed.</param><param name="fModified">[in] true if any update action succeeded.</param><param name="fCancelCommand">[in] true if update actions were canceled.</param>
        int IVsUpdateSolutionEvents.UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
        {
            // This method is called when a build is completed.
            this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: UpdateSolution_Done");

            this.activeBuildTarget = null;
            return 0;
        }

        /// <summary>
        /// Called before the first project configuration is about to be built.
        /// </summary>
        /// <returns>
        /// If the method succeeds, it returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"/>. If it fails, it returns an error code.
        /// </returns>
        /// <param name="pfCancelUpdate">[in, out] Pointer to a flag indicating cancel update.</param>
        int IVsUpdateSolutionEvents.UpdateSolution_StartUpdate(ref int pfCancelUpdate)
        {
            // This method is called when the entire solution starts to build.
            this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: UpdateSolution_StartUpdate");

            string solutionFilePath = this.dte2.Solution.FullName;
            string solutionFolder = Path.GetDirectoryName(solutionFilePath);
            string solutionFileName = Path.GetFileName(solutionFilePath);
            string targetsFileName = "after." + solutionFileName + ".targets";
            if (solutionFolder == null)
                return VSConstants.E_UNEXPECTED;

            string targetsFilePath = Path.Combine(solutionFolder, targetsFileName);

            if (!File.Exists(targetsFilePath))
                return VSConstants.S_OK;

            string solutionConfigurationName = this.dte2.Solution.SolutionBuild.ActiveConfiguration.Name;
            this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: active solution configuration name is \"{0}\"", solutionConfigurationName);

            List<ILogger> loggers = new List<ILogger> { this.MakeBuildLogger() };

            ProjectInstance solutionInitProjectInstance;
            try
            {
                solutionInitProjectInstance = new ProjectInstance(targetsFilePath);
            }
            catch (Exception ex)
            {
                this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: failed to load targets file \"{0}\", Exception: {1}", targetsFilePath, ex);
                return VSConstants.E_FAIL;
            }
            solutionInitProjectInstance.SetProperty("Configuration", solutionConfigurationName);
            solutionInitProjectInstance.SetProperty("BuildingInsideVisualStudio", "true");
            int numberOfPropertiesBeforeBuild = solutionInitProjectInstance.Properties.Count;
            this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: building targets file \"{0}\", target \"{1}\"", targetsFilePath, this.activeBuildTarget);
            solutionInitProjectInstance.Build(this.activeBuildTarget, loggers);

            // If solution targets build produced new custom properties, fetch those and add them to the global properties collection.
            // Most typical usage for this feature is setting "CustomAfterMicrosoftCommontargets" property.
            for (int propertyNumber = numberOfPropertiesBeforeBuild;
                propertyNumber < solutionInitProjectInstance.Properties.Count;
                propertyNumber++)
            {
                ProjectPropertyInstance property = solutionInitProjectInstance.Properties.ElementAt(propertyNumber);
                if (property.Name.StartsWith("Custom"))
                {
                    this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: defined global build property {0} = {1}", property.Name, property.EvaluatedValue);
                    ProjectCollection.GlobalProjectCollection.SetGlobalProperty(property.Name, property.EvaluatedValue);
                }
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called when a build is being cancelled.
        /// </summary>
        int IVsUpdateSolutionEvents.UpdateSolution_Cancel()
        {
            return 0;
        }

        /// <summary>
        /// Called when the active project configuration for a project in the solution has changed.
        /// </summary>
        /// <returns>
        /// If the method succeeds, it returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"/>. If it fails, it returns an error code.
        /// </returns>
        /// <param name="pIVsHierarchy">[in] Pointer to an <see cref="T:Microsoft.VisualStudio.Shell.Interop.IVsHierarchy"/> object.</param>
        int IVsUpdateSolutionEvents.OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy)
        {
            this.WriteLine(LoggerVerbosity.Detailed, "CommonSolutionTargets: OnActiveProjectCfgChange");
            return 0;
        }

        #endregion

        /// <summary>
        /// Outputs a message to the debug output pane, if the VS MSBuildOutputVerbosity
        /// setting value is greater than or equal to the given verbosity. So if verbosity is 0,
        /// it means the message is always written to the output pane.
        /// </summary>
        /// <param name="verbosity">The verbosity level.</param>
        /// <param name="format">The format string.</param>
        /// <param name="args">An array of objects to write using format. </param>
        private void WriteLine(LoggerVerbosity verbosity, string format, params object[] args)
        {
            if (this.buildOutputWindowPane == null)
                return;

            if ((int)this.currentLoggerVerbosity < (int)verbosity)
                return;


            this.buildOutputWindowPane.OutputString(string.Format(format + Environment.NewLine, args));
        }

        private ILogger MakeBuildLogger()
        {
            this.RefreshMSBuildOutputVerbositySetting();
            this.WriteLine(LoggerVerbosity.Diagnostic, "CommonSolutionTargets: creating build logger with verbosity {0}", this.currentLoggerVerbosity);
            return new IDEBuildLogger(this.buildOutputWindowPane, this.currentLoggerVerbosity);
        }

        /// <summary>
        /// Refreshes the value of the VisualStudio MSBuildOutputVerbosity setting.
        /// </summary>
        /// <remarks>
        /// 0 is Quiet, while 4 is diagnostic.
        /// </remarks>
        private void RefreshMSBuildOutputVerbositySetting()
        {
            Properties properties = this.dte2.Properties["Environment", "ProjectsAndSolution"];
            this.currentLoggerVerbosity = (LoggerVerbosity)properties.Item("MSBuildOutputVerbosity").Value;
        }
    }

    internal class IDEBuildLogger : ConsoleLogger
    {
        private IVsOutputWindowPane BuildOutputWindowPane { get; set; }
        internal IDEBuildLogger(IVsOutputWindowPane buildOutputWindowPane, LoggerVerbosity verbosity)
        {
            this.BuildOutputWindowPane = buildOutputWindowPane;
            this.WriteHandler = this.WriteToOutputWindowBuildPane;

            this.ShowSummary = false;
            this.SkipProjectStartedText = true;
            this.Verbosity = verbosity;
        }

        private void WriteToOutputWindowBuildPane(string message)
        {
            this.BuildOutputWindowPane.OutputString(message);
        }
    }
}
