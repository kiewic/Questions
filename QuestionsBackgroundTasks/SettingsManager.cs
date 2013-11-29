﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;
using Windows.Web.Syndication;

namespace QuestionsBackgroundTasks
{
    public sealed class SettingsManager
    {
        private static JsonObject rootObject;
        private static JsonObject websitesCollection;
        private static ApplicationDataContainer roamingSettings;

        public static DateTimeOffset LastAllRead
        {
            get
            {
                CheckSettingsAreLoaded();

                if (roamingSettings.Values.ContainsKey("LastAllRead"))
                {
                    return DateTimeOffset.Parse(roamingSettings.Values["LastAllRead"].ToString());
                }

                return DateTime.MinValue;
            }
            set
            {
                roamingSettings.Values["LastAllRead"] = value.ToString();
            }
        }

        public static string Version
        {
            get
            {
                return roamingSettings.Values["Version"].ToString();
            }
        }

        public static IAsyncAction LoadAsync()
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                if (roamingSettings != null)
                {
                    // Settings already loaded, there is nothing to load.
                    return;
                }

                roamingSettings = ApplicationData.Current.RoamingSettings;

                Debug.WriteLine("Roaming storage quota: {0} KB", ApplicationData.Current.RoamingStorageQuota);
                Debug.WriteLine("Roaming folder: {0}", ApplicationData.Current.RoamingFolder.Path);

                InitializeRoamingSettings();
            });
        }

        private static void InitializeRoamingSettings()
        {
            if (!roamingSettings.Values.ContainsKey("Version"))
            {
                roamingSettings.Values["Version"] = "3";
            }

            if (!roamingSettings.Values.ContainsKey("UserId"))
            {
                // Generate a random user id.
                roamingSettings.Values["UserId"] = Guid.NewGuid().ToString();
            }

            if (!roamingSettings.Values.ContainsKey("Websites"))
            {
                JsonObject jsonObject = new JsonObject();
                roamingSettings.Values["Websites"] = jsonObject.Stringify();
            }

            // Parse websites.
            string jsonString = roamingSettings.Values["Websites"].ToString();
            websitesCollection = JsonObject.Parse(jsonString);
        }

        // TODO: Make this method synchronous.
        public static void Save()
        {
            roamingSettings.Values["Websites"] = websitesCollection.Stringify();
        }

        public static IAsyncOperation<BindableWebsite> AddWebsiteAndSave(BindableWebsiteOption websiteOption)
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                const int websitesLimit = 10;

                CheckSettingsAreLoaded();

                string websiteSiteUrl = websiteOption.SiteUrl;

                JsonObject websiteObject = null;
                if (websitesCollection.ContainsKey(websiteSiteUrl))
                {
                    // We already have this website. Nothing to do.
                    websiteObject = websitesCollection.GetNamedObject(websiteSiteUrl);
                }
                else if (websitesCollection.Count < websitesLimit)
                {
                    websiteObject = new JsonObject();
                    websiteObject.SetNamedValue("Tags", new JsonObject());
                    websiteObject.SetNamedValue("Name", JsonValue.CreateStringValue(websiteOption.ToString()));
                    websiteObject.SetNamedValue("ApiSiteParameter", JsonValue.CreateStringValue(websiteOption.ApiSiteParameter));
                    websiteObject.SetNamedValue("IconUrl", JsonValue.CreateStringValue(websiteOption.IconUrl));
                    websiteObject.SetNamedValue("FaviconUrl", JsonValue.CreateStringValue(websiteOption.FaviconUrl));
                    websitesCollection.SetNamedValue(websiteSiteUrl, websiteObject);

                    Save();
                }
                else
                {
                    var dialog = new MessageDialog("Only 10 websites allowed.", "Oops.");
                    await dialog.ShowAsync();

                    // Make sure to return null.
                    return null;
                }

                return new BindableWebsite(websiteSiteUrl, websiteObject);
            });
        }

        public static void DeleteWebsiteAndSave(BindableWebsite website)
        {
            CheckSettingsAreLoaded();

            websitesCollection.Remove(website.ToString());

            // TODO: Remove only questions containing this website.
            QuestionsManager.ClearQuestions();

            Save();
        }

        internal static IEnumerable<string> GetWebsiteKeys()
        {
            return websitesCollection.Keys;
        }

        internal static string GetWebsiteFaviconUrl(string website)
        {
            if (websitesCollection.ContainsKey(website))
            {
                JsonObject websiteObject = websitesCollection.GetNamedObject(website);
                if (websiteObject.ContainsKey("FaviconUrl"))
                {
                    return websiteObject.GetNamedString("FaviconUrl");
                }
            }

            return "";
        }

        public static async void LoadAndDisplayWebsites(ListView listView)
        {
            await LoadAsync();

            listView.Items.Clear();
            foreach (var keyValuePair in websitesCollection)
            {
                var website = new BindableWebsite(keyValuePair.Key, keyValuePair.Value.GetObject());
                listView.Items.Add(website);
            }
        }

        public static string ConcatenateAllTags(string website)
        {
            CheckSettingsAreLoaded();

            JsonObject websiteObject = websitesCollection.GetNamedObject(website);
            JsonObject tagsCollection = websiteObject.GetNamedObject("Tags");

            StringBuilder builder = new StringBuilder();
            foreach (string tag in tagsCollection.Keys)
            {
                if (builder.Length != 0)
                {
                    builder.Append(" OR ");
                }
                builder.Append(WebUtility.UrlEncode(tag));
            }
            return builder.ToString();
        }

        public static bool TryCreateUri(string website, string query, out Uri uri)
        {
            // If the query is empty, return the main feed URI.
            string uriString = website + "/feeds";

            // If the query is not empty, ask for the feed of the specified tags.
            if (!String.IsNullOrEmpty(query))
            {
                uriString += "/tag/" + query;
            }

            return Uri.TryCreate(uriString, UriKind.Absolute, out uri);
        }

        public static IAsyncOperation<bool> IsEmptyAsync()
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                await LoadAsync();

                return (websitesCollection.Count == 0) ? true : false;
            });
        }

        private static void CheckSettingsAreLoaded()
        {
            if (roamingSettings == null)
            {
                throw new Exception("Settings not loaded.");
            }
        }

        public static async void Export(StorageFile file)
        {
            CheckSettingsAreLoaded();

            JsonObject jsonObject = new JsonObject();

            foreach (KeyValuePair<string, object> pair in roamingSettings.Values)
            {
                IJsonValue jsonValue;
                if (pair.Value == null)
                {
                    jsonValue = JsonValue.Parse("null");
                }
                else if (pair.Value is string)
                {
                    jsonValue = JsonValue.CreateStringValue((string)pair.Value);
                }
                else if (pair.Value is int)
                {
                    jsonValue = JsonValue.CreateNumberValue((int)pair.Value);
                }
                else if (pair.Value is double)
                {
                    jsonValue = JsonValue.CreateNumberValue((double)pair.Value);
                }
                else if (pair.Value is bool)
                {
                    jsonValue = JsonValue.CreateBooleanValue((bool)pair.Value);
                }
                else
                {
                    throw new Exception("Not supported type.");
                }

                jsonObject.Add(pair.Key, jsonValue);
            }

            await FileIO.WriteTextAsync(file, jsonObject.Stringify());
        }

        public static async void Import(StorageFile file)
        {
            string jsonString = await FileIO.ReadTextAsync(file);

            JsonObject jsonObject;
            if (!JsonObject.TryParse(jsonString, out jsonObject))
            {
                // TODO: Notify user there was an error.
                throw new Exception("Invalid JSON string.");
            }

            roamingSettings.Values.Clear();

            foreach (string key in jsonObject.Keys)
            {
                IJsonValue jsonValue = jsonObject[key];

                switch (jsonValue.ValueType)
                {
                    case JsonValueType.String:
                        roamingSettings.Values.Add(key, jsonObject[key].GetString());
                        break;
                    case JsonValueType.Number:
                        roamingSettings.Values.Add(key, jsonObject[key].GetNumber());
                        break;
                    case JsonValueType.Boolean:
                        roamingSettings.Values.Add(key, jsonObject[key].GetBoolean());
                        break;
                    case JsonValueType.Null:
                        roamingSettings.Values.Add(key, null);
                        break;
                    default:
                        throw new Exception("Not supported JsonValueType.");
                }
            }

            // Any value may be missing from the settings file, make sure all
            // values are initialized and websites are parsed.
            InitializeRoamingSettings();
        }
    }
}