﻿using QuestionsBackgroundTasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.ApplicationSettings;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Application template is documented at http://go.microsoft.com/fwlink/?LinkId=234227

namespace Questions
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        private TypedEventHandler<Accelerometer, AccelerometerShakenEventArgs> shakenHandler;
        private Accelerometer accelerometer;
        private bool easterEggState = false;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;

            RegisterShaken();
        }

        private void RegisterShaken()
        {
            shakenHandler = new TypedEventHandler<Accelerometer, AccelerometerShakenEventArgs>(OnShaken);
            accelerometer = Accelerometer.GetDefault();
            if (accelerometer != null)
            {
                accelerometer.Shaken += shakenHandler;
            }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used when the application is launched to open a specific file, to display
        /// search results, and so forth.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                if (args.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                // When the navigation stack isn't restored navigate to the first page,
                // configuring the new page by passing required information as a navigation
                // parameter

                // Maybe we received an unpdated "read list" while the app was not running. Make sure
                // to remove the questions that are listed in the "read list".
                SettingsManager.Load();
                await QuestionsManager.LoadAsync();
                await QuestionsManager.RemoveReadQuestionsUpdateTileAndBadgeAndSaveAsync();

                bool isNewApp = SettingsManager.IsEmpty();
                Type initialPage = typeof(ItemsPage);
                if (isNewApp)
                {
                    initialPage = typeof(TutorialPage);
                }

                if (!rootFrame.Navigate(initialPage, args.Arguments))
                {
                    throw new Exception("Failed to create initial page");
                }
            }
            // Ensure the current window is active
            Window.Current.Activate();

            // Add privacy policy command.
            SettingsPane.GetForCurrentView().CommandsRequested += AddSettingsCommand;
        }

        private void AddSettingsCommand(SettingsPane sender, SettingsPaneCommandsRequestedEventArgs args)
        {
            SettingsCommand tutorialCommand = new SettingsCommand("Tutorial", "See Tutorial", SeeTurorial);
            SettingsCommand privacyPolicyCommand = new SettingsCommand("PrivacyPolicy", "Privacy Policy", LaunchPrivacyPolicyUrl);
            SettingsCommand importSettingsCommand = new SettingsCommand("ImportSettings", "Import Settings", ImportSettings);
            SettingsCommand exportSettingsCommand = new SettingsCommand("ExportSettings", "Export Settings", ExportSettings);
            args.Request.ApplicationCommands.Add(tutorialCommand);
            args.Request.ApplicationCommands.Add(privacyPolicyCommand);
            args.Request.ApplicationCommands.Add(importSettingsCommand);
            args.Request.ApplicationCommands.Add(exportSettingsCommand);
        }

        // TODO: Add an About flyout and display the version.
        private string GetVersion()
        {
            var ver = Windows.ApplicationModel.Package.Current.Id.Version;
            return String.Format(
                "Version {0}.{1}.{2}.{3}",
                ver.Major.ToString(),
                ver.Minor.ToString(),
                ver.Build.ToString(),
                ver.Revision.ToString());
        }

        private void SeeTurorial(IUICommand command)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            rootFrame.Navigate(typeof(TutorialPage));
        }

        async void LaunchPrivacyPolicyUrl(IUICommand uiCommand)
        {
            Uri privacyPolicyUri = new Uri("http://kiewic.com/privacypolicy");
            var result = await Windows.System.Launcher.LaunchUriAsync(privacyPolicyUri);
        }

        private async void ImportSettings(IUICommand command)
        {
            FileOpenPicker openPicker = new FileOpenPicker();
            openPicker.ViewMode = PickerViewMode.List;
            openPicker.SuggestedStartLocation = PickerLocationId.Downloads;
            openPicker.FileTypeFilter.Add(".json");
            StorageFile file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                SettingsManager.ImportAndSave(file);
            }
        }

        private async void ExportSettings(IUICommand command)
        {
            FileSavePicker savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.Downloads;
            DateTime now = DateTime.Now;
            savePicker.SuggestedFileName = String.Format(
                "questions_{0:D4}_{1:D2}_{2:D2}_at_{3:D2}_{4:D2}.json",
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                now.Minute);
            savePicker.FileTypeChoices.Add("JSON", new string[] { ".json" });
            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                SettingsManager.Export(file);
            }
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }


        private void OnShaken(Accelerometer sender, AccelerometerShakenEventArgs args)
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            var runOperation = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                var content = Window.Current.Content;
                Frame rootFrame = content as Frame;
                if (easterEggState)
                {
                    rootFrame.Navigate(typeof(ItemsPage));
                }
                else
                {
                    rootFrame.Navigate(typeof(EasterEggPage));
                }
                easterEggState = !easterEggState;
            });
        }
    }
}
