using AvatarFav.IL;
using AvatarFav.Model;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRC.UI;
using VRCModLoader;
using VRCModNetwork;
using VRCSDK2;
using VRCTools;

namespace AvatarFav
{
    [VRCModInfo("AvatarFavLocal", "1.3.4", "Anonymous-BCFED")]
    public class AvatarFavMod : VRCMod
    {
        public static AvatarFavMod instance;

        private static string _apiKey = null;
        private static int _buildNumber = -1;


        //private static MethodInfo updateAvatarListMethod;

        //internal static List<ApiAvatar> favoriteAvatarList = new List<ApiAvatar>();
        internal static List<string> favoriteAvatarList = new List<string>();
        private static bool avatarAvailables = false;
        private bool freshUpdate = false;
        private bool waitingForServer = false;
        private bool useNetwork = false;
        private bool useNetworkLastState = false;
        private bool newAvatarsFirst = true;
        private static UiAvatarList favList;
        private static Transform favButton;
        private static Text favButtonText;
        private static PageAvatar pageAvatar;
        private static Transform avatarModel;
        private static FieldInfo applyAvatarField;

        private static Vector3 baseAvatarModelPosition;
        private static string currentUiAvatarId = "";

        private bool alreadyLoaded = false;
        private bool initialised = false;
        private string addError;

        private Button.ButtonClickedEvent baseChooseEvent;

        private ApiWorld currentRoom;
        private UiAvatarList avatarSearchList;
        private UiInputField searchbar;


        internal static FieldInfo categoryField;
        private List<Action> actions = new List<Action>();

        internal static Dictionary<string, SerializableApiAvatar> savedFavoriteAvatars = new Dictionary<string, SerializableApiAvatar>();
        private Transform syncButton;
        private Text syncButtonText;
        private static bool avatarsJSONLoaded = false;

        void OnApplicationStart()
        {
            VRCTools.ModPrefs.RegisterCategory("avatarfav", "AvatarFavLocal");
            VRCTools.ModPrefs.RegisterPrefBool("avatarfav", "newavatarsfirst", newAvatarsFirst, "Show new avatars first");
            VRCTools.ModPrefs.RegisterPrefBool("avatarfav", "useVRCToolsNetwork", useNetwork, "Use VRCTools network at all"); //Local

            newAvatarsFirst = VRCTools.ModPrefs.GetBool("avatarfav", "newavatarsfirst");
            useNetwork = VRCTools.ModPrefs.GetBool("avatarfav", "useVRCToolsNetwork");

            categoryField = typeof(UiAvatarList).GetField("category", BindingFlags.Public | BindingFlags.Instance);
        }

        void OnLevelWasLoaded(int level)
        {
            VRCModLogger.Log("[AvatarFav] OnLevelWasLoaded (" + level + ")");
            if (level == (Application.platform == RuntimePlatform.WindowsPlayer ? 1 : 2) && !alreadyLoaded)
            {
                alreadyLoaded = true;

                if (instance != null)
                {
                    Debug.LogWarning("[AvatarFav] Trying to load the same plugin twice!");
                    return;
                }
                instance = this;
                VRCModLogger.Log("[AvatarFav] Adding button to UI - Looking up for Change Button");
                // Add a "Favorite" / "Unfavorite" button over the "Choose" button of the AvatarPage
                Transform changeButton = null;
                pageAvatar = Resources.FindObjectsOfTypeAll<PageAvatar>().First(p => (changeButton = p.transform.Find("Change Button")) != null);

                VRCModLogger.Log("[AvatarFav] Adding avatar check on Change button");

                baseChooseEvent = changeButton.GetComponent<Button>().onClick;

                changeButton.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
                changeButton.GetComponent<Button>().onClick.AddListener(() =>
                {
                    VRCModLogger.Log("[AvatarFav] Fetching avatar releaseStatus for " + pageAvatar.avatar.apiAvatar.name + " (" + pageAvatar.avatar.apiAvatar.id + ")");
                    ModManager.StartCoroutine(CheckAndWearAvatar());
                });



                VRCModLogger.Log("[AvatarFav] Adding favorite button to UI - Duplicating Button");
                favButton = UnityUiUtils.DuplicateButton(changeButton, "Favorite", new Vector2(0, 80));
                favButton.name = "ToggleFavorite";
                favButton.gameObject.SetActive(false);
                favButtonText = favButton.Find("Text").GetComponent<Text>();
                favButton.GetComponent<Button>().interactable = false;
                favButton.GetComponent<Button>().onClick.AddListener(ToggleAvatarFavorite);

                VRCModLogger.Log("[AvatarFav] Adding sync button to UI - Duplicating Button");
                syncButton = UnityUiUtils.DuplicateButton(changeButton, "Sync", new Vector2(0, 100));
                syncButton.name = "SyncFavorites";
                syncButton.gameObject.SetActive(false);
                syncButtonText = syncButton.Find("Text").GetComponent<Text>();
                syncButton.GetComponent<Button>().interactable = false;
                syncButton.GetComponent<Button>().onClick.AddListener(SyncWithNetwork);

                VRCModLogger.Log("[AvatarFav] Storing default AvatarModel position");
                avatarModel = pageAvatar.transform.Find("AvatarModel");
                baseAvatarModelPosition = avatarModel.localPosition;

                FileInfo[] files = new DirectoryInfo(Environment.CurrentDirectory).GetFiles("Avatars.txt", SearchOption.AllDirectories);
                VRCModLogger.Log("[AvatarFavMod] Found " + files.Length + " Avatars.txt");
                if (files.Length > 0)
                {
                    VRCModLogger.Log("[AvatarFav] Adding import button to UI - Duplicating Button");
                    Transform importButton = UnityUiUtils.DuplicateButton(changeButton, "Import Avatars", new Vector2(0, 0));
                    importButton.name = "ImportAvatars";

                    importButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(560, 371);

                    importButton.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Do you want to import the public avatars from your VRCheat avatar list ?",
                        "Yes", () =>
                        {
                            ModManager.StartCoroutine(VRCheatAvatarfileImporter.ImportAvatarfile());
                        },
                        "No", () =>
                        {
                            VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
                        });
                        VRCheatAvatarfileImporter.ImportAvatarfile();
                    });

                }


                favList = AvatarPageHelper.AddNewList("Favorite Avatar List (Unofficial)", 1);

                // Get Getter of VRCUiContentButton.PressAction
                applyAvatarField = typeof(VRCUiContentButton).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).First((field) => field.FieldType == typeof(Action));

                VRCModLogger.Log("[AvatarFav] Registering VRCModNetwork events");
                LoadAvatars();

                VRCModNetworkManager.OnAuthenticated += () =>
                {
                    RequestAvatarsFromRPC();
                };

                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.serverconnected", (senderId, data) => { 
                    if (waitingForServer) 
                        RequestAvatarsFromRPC(); 
                });
                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.error", (senderId, data) => addError = data);
                VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.avatarlistupdated", (senderId, data) =>
                {
                    handleFreshJSON(data);
                });




                VRCModLogger.Log("[AvatarFav] Adding avatar search list");

                if (pageAvatar != null)
                {
                    VRCUiPageHeader pageheader = VRCUiManagerUtils.GetVRCUiManager().GetComponentInChildren<VRCUiPageHeader>(true);
                    if (pageheader != null)
                    {
                        searchbar = pageheader.searchBar;
                        if (searchbar != null)
                        {
                            VRCModLogger.Log("[AvatarFav] creating avatar search list");
                            avatarSearchList = AvatarPageHelper.AddNewList("Search Results", 0);
                            avatarSearchList.ClearAll();
                            avatarSearchList.gameObject.SetActive(false);
                            avatarSearchList.collapsedCount = 50;
                            avatarSearchList.expandedCount = 50;
                            avatarSearchList.collapseRows = 5;
                            avatarSearchList.extendRows = 5;
                            avatarSearchList.contractedHeight = 850f;
                            avatarSearchList.expandedHeight = 850f;
                            avatarSearchList.GetComponent<LayoutElement>().minWidth = 1600f;
                            avatarSearchList.GetComponentInChildren<GridLayoutGroup>(true).constraintCount = 5;
                            avatarSearchList.expandButton.image.enabled = false;

                            VRCModLogger.Log("[AvatarFav] Overwriting search button");
                            VRCUiManagerUtils.OnPageShown += (page) =>
                            {
                                if (page.GetType() == typeof(PageAvatar))
                                {
                                    UiVRCList[] lists = page.GetComponentsInChildren<UiVRCList>(true);
                                    foreach(UiVRCList list in lists)
                                    {
                                        if (list != avatarSearchList && (list.GetType() != typeof(UiAvatarList) || ((int)categoryField.GetValue(list)) != 0))
                                            list.gameObject.SetActive(true);
                                        else
                                            list.gameObject.SetActive(false);
                                    }
                                    if (useNetwork)
                                    {
                                        VRCModLogger.Log("[AvatarFav] PageAvatar shown. Enabling searchbar next frame");
                                        ModManager.StartCoroutine(EnableSearchbarNextFrame());
                                    }
                                }
                            };

                            VRCModNetworkManager.SetRPCListener("slaynash.avatarfav.searchresults", (senderid, data) =>
                            {
                                AddMainAction(() =>
                                {
                                    if (data.StartsWith("ERROR"))
                                    {
                                        VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Unable to fetch avatars: Server returned error: " + data.Substring("ERROR ".Length), "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                                    }
                                    else
                                    {
                                        avatarSearchList.ClearSpecificList();
                                        if (!avatarSearchList.gameObject.activeSelf)
                                        {
                                            UiVRCList[] lists = pageAvatar.GetComponentsInChildren<UiVRCList>(true);
                                            foreach (UiVRCList list in lists)
                                            {
                                                if (list != avatarSearchList)
                                                    list.gameObject.SetActive(false);
                                            }
                                        }

                                        SerializableApiAvatar[] serializedAvatars = SerializableApiAvatar.ParseJson(data);

                                        string[] avatarsIds = new string[serializedAvatars.Length];

                                        for (int i = 0; i < serializedAvatars.Length; i++) avatarsIds[i] = serializedAvatars[i].id;

                                        avatarSearchList.specificListIds = avatarsIds;
                                        if (avatarSearchList.gameObject.activeSelf)
                                            avatarSearchList.Refresh();
                                        else
                                            avatarSearchList.gameObject.SetActive(true);
                                    }
                                });
                            });
                        }
                        else
                            VRCModLogger.LogError("[AvatarFav] Unable to find search bar");
                    }
                    else
                        VRCModLogger.LogError("[AvatarFav] Unable to find page header");
                }
                else
                    VRCModLogger.LogError("[AvatarFav] Unable to find avatar page");





                VRCModLogger.Log("[AvatarFav] AvatarFav Initialised !");
                initialised = true;
            }
        }

        private void SyncWithNetwork()
        {
            VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "This isn't ready yet, sorry.", "OK", () =>
            {
                VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
            });
        }

        private void handleFreshJSON(string data, bool nosave=false, bool overwrite=true)
        {
            lock (favoriteAvatarList)
            {
                lock (savedFavoriteAvatars)
                {
                    // Update Ui
                    favButton.GetComponent<Button>().interactable = true;
                    SimpleAvatarList avatarList = SimpleAvatarList.ParseJSON(data);
                    //favoriteAvatarList.Clear();
                    foreach (var id in avatarList.avatarIDs)
                    {
                        if(!favoriteAvatarList.Contains(id))
                            favoriteAvatarList.Add(id);
                        if(!savedFavoriteAvatars.ContainsKey(id) || overwrite)
                            savedFavoriteAvatars[id] = new SerializableApiAvatar() { id = id, authorId="", releaseStatus="" };
                    }
                    if(!nosave)
                        Save();
                    avatarAvailables = true;
                }
            }
        }

        private void AddMainAction(Action a)
        {
            lock (actions)
            {
                actions.Add(a);
            }
        }

        private IEnumerator EnableSearchbarNextFrame()
        {
            yield return null;
            searchbar.editButton.interactable = true;
            searchbar.onDoneInputting = (text) =>
            {
                SearchForAvatars(text);
            };
        }

        private IEnumerator DisableSearchbarNextFrame()
        {
            yield return null;
            searchbar.editButton.interactable = false;
            searchbar.onDoneInputting = (text) =>
            {
                //SearchForAvatars(text);
            };
        }

        private void SearchForAvatars(string text)
        {
            if (!useNetwork || waitingForServer)
                return;
            VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Looking for avatars");
            VRCModNetworkManager.SendRPC("slaynash.avatarfav.search", text.Trim(), () => { }, (error) => {
                VRCUiPopupManagerUtils.ShowPopup("AvatarFav", "Unable to fetch avatars:\nVRVCModNetwork returned error:\n" + error, "Close", () =>  VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
            });
        }

        public void OnUpdate()
        {
            newAvatarsFirst = VRCTools.ModPrefs.GetBool("avatarfav", "newavatarsfirst");
            useNetwork = VRCTools.ModPrefs.GetBool("avatarfav", "useVRCToolsNetwork");

            if (!initialised) return;

            lock (actions)
            {
                foreach (Action a in actions)
                {
                    try
                    {
                        a();
                    }
                    catch (Exception e)
                    {
                        VRCModLogger.Log("[AvatarFav] Error while calling action from main thread: " + e);
                    }
                }
                actions.Clear();
            }

            // Local: If useNetwork changed from lastState, check if we need to re-init connections.
            if (useNetwork != useNetworkLastState)
            {
                if (useNetwork)
                {
                    VRCModLogger.Log("[AvatarFav] useNetwork reset to true! Enabling search bar next frame.");
                    ModManager.StartCoroutine(EnableSearchbarNextFrame());
                } else
                {

                }
                useNetworkLastState = useNetwork;
            }
            try
            {
                //Update list if element is active
                if (favList.gameObject.activeInHierarchy)
                {
                    lock (favoriteAvatarList)
                    {
                        if (avatarAvailables)
                        {
                            avatarAvailables = false;
                            favList.ClearSpecificList();

                            if (newAvatarsFirst)
                            {
                                List<string> favReversed = favoriteAvatarList.ToList();
                                favReversed.Reverse();
                                favList.specificListIds = favReversed.ToArray();
                            }
                            else
                                favList.specificListIds = favoriteAvatarList.ToArray();


                            favList.Refresh();

                            freshUpdate = true;
                        }
                    }
                }

                if (pageAvatar.avatar != null && pageAvatar.avatar.apiAvatar != null && CurrentUserUtils.GetGetCurrentUser().GetValue(null) != null && !currentUiAvatarId.Equals(pageAvatar.avatar.apiAvatar.id) || freshUpdate)
                {
                    currentUiAvatarId = pageAvatar.avatar.apiAvatar.id;

                    bool favorited = favoriteAvatarList.Contains(currentUiAvatarId);

                    if (favorited)
                        favButtonText.text = "Unfavorite";
                    else
                        favButtonText.text = "Favorite";

                    if ((!pageAvatar.avatar.apiAvatar.releaseStatus.Equals("public") && !favorited) || pageAvatar.avatar.apiAvatar.authorId == APIUser.CurrentUser.id)
                    {
                        favButton.gameObject.SetActive(false);
                        avatarModel.localPosition = baseAvatarModelPosition;
                    }
                    else
                    {
                        favButton.gameObject.SetActive(true);
                        avatarModel.localPosition = baseAvatarModelPosition + new Vector3(0, 60, 0);
                    }
                }

                //Show returned error if exists
                if(addError != null)
                {
                    VRCUiPopupManagerUtils.ShowPopup("Error", addError, "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                    addError = null;
                }


                if (RoomManager.currentRoom != null && RoomManager.currentRoom.id != null && RoomManager.currentRoom.currentInstanceIdOnly != null)
                {
                    if(currentRoom == null)
                    {
                        currentRoom = RoomManager.currentRoom;

                        if (currentRoom.releaseStatus != "public")
                        {
                            VRCModLogger.Log("[AvatarFav] Current world release status isn't public. Pedestal scan disabled.");
                        }
                        else
                        {
                            VRC_AvatarPedestal[] pedestalsInWorld = GameObject.FindObjectsOfType<VRC_AvatarPedestal>();
                            VRCModLogger.Log("[AvatarFav] Found " + pedestalsInWorld.Length + " VRC_AvatarPedestal in current world");
                            if (useNetwork)
                            {
                                string dataToSend = currentRoom.id;
                                foreach (VRC_AvatarPedestal p in pedestalsInWorld)
                                {
                                    if (p.blueprintId == null || p.blueprintId == "")
                                        continue;

                                    dataToSend += ";" + p.blueprintId;
                                }

                                VRCModNetworkManager.SendRPC("slaynash.avatarfav.avatarsinworld", dataToSend);
                            } else {
                                VRCModLogger.Log("[AvatarFav]  useNetwork=false, skipping avatarsinworld RPC call.");
                            }
                        }
                    }
                }
                else
                {
                    currentRoom = null;
                }


            }
            catch (Exception e)
            {
                VRCModLogger.Log("[AvatarFav] [ERROR] " + e.ToString());
            }
            freshUpdate = false;
        }



        private IEnumerator CheckAndWearAvatar()
        {
            //DebugUtils.PrintHierarchy(pageAvatar.avatar.transform, 0);
            PipelineManager avatarPipelineManager = pageAvatar.avatar.GetComponentInChildren<PipelineManager>();
            if (avatarPipelineManager == null) // Avoid avatars locking for builds <625
            {
                VRCModLogger.Log("[AvatarFav] Current avatar prefab name: " + pageAvatar.avatar.transform.GetChild(0).name);
                if (pageAvatar.avatar.transform.GetChild(0).name == "avatar_loading2(Clone)")
                    VRCUiPopupManagerUtils.ShowPopup("Error", "Please wait for this avatar to finish loading before wearing it", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                else
                    baseChooseEvent.Invoke();
            }
            else
            {
                bool copied = false;

                string avatarBlueprintID = avatarPipelineManager?.blueprintId ?? "";
                if (!avatarBlueprintID.Equals("") && !avatarBlueprintID.Equals(pageAvatar.avatar.apiAvatar.id))
                    copied = true;

                using (WWW avtrRequest = new WWW(API.GetApiUrl() + "avatars/" + (copied ? avatarBlueprintID : pageAvatar.avatar.apiAvatar.id) + "?apiKey=" + GetApiKey()))
                {
                    yield return avtrRequest;
                    int rc = WebRequestsUtils.GetResponseCode(avtrRequest);
                    if (rc == 200)
                    {
                        try
                        {
                            string uuid = APIUser.CurrentUser?.id ?? "";
                            SerializableApiAvatar aa = JsonConvert.DeserializeObject<SerializableApiAvatar>(avtrRequest.text);
                            if (aa.releaseStatus.Equals("public") || aa.authorId.Equals(uuid))
                            {
                                baseChooseEvent.Invoke();
                            }
                            else
                            {
                                if(copied)
                                    VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not the original one, and the one is not public anymore (private)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                                else VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not public anymore (private)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                            }
                        }
                        catch (Exception e)
                        {
                            VRCModLogger.LogError(e.ToString());
                            VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: Unable to parse API response", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                        }
                    }
                    else
                    {
                        if (copied)
                            VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not the original one, and the one is not public anymore (deleted)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                        else VRCUiPopupManagerUtils.ShowPopup("Error", "Unable to put this avatar: This avatar is not public anymore (deleted)", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                    }
                }
            }
        }

        internal static string GetApiKey()
        {
            if(_apiKey == null)
            {
                VRCModLogger.Log("[AvatarFav] Trying to get ApiKey from VRC.Core.API");
                foreach (FieldInfo fi_ in typeof(API).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (fi_ != null && fi_.Name == "ApiKey")
                    {
                        _apiKey = fi_.GetValue(null) as string;
                        break;
                    }
                }
                if(_apiKey == null)
                {
                    VRCModLogger.Log("[AvatarFav] Unable to find field ApiKey in VRC.Core.API. Using default key.");
                    _apiKey = "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26";
                }
                VRCModLogger.Log("[AvatarFav] Current ApiKey: " + _apiKey);
            }
            return _apiKey;
        }

        internal static int GetBuildNumber()
        {
            if (_buildNumber == -1)
            {
                VRCModLogger.Log("[AvatarFav] Fetching build number");
                PropertyInfo vrcApplicationSetupInstanceProperty = typeof(VRCApplicationSetup).GetProperties(BindingFlags.Public | BindingFlags.Static).First((pi) => pi.PropertyType == typeof(VRCApplicationSetup));
                _buildNumber = ((VRCApplicationSetup)vrcApplicationSetupInstanceProperty.GetValue(null, null)).buildNumber;
                VRCModLogger.Log("[AvatarFav] Game build " + _buildNumber);
            }
            return _buildNumber;
        }
        /*
        private void UpdateFavList()
        {
            object[] parameters = new object[] { favoriteAvatarList };
            updateAvatarListMethod.Invoke(favList, parameters);
        }
        */
        private void ToggleAvatarFavorite()
        {
            ApiAvatar currentApiAvatar = pageAvatar.avatar.apiAvatar;
            //Check if the current avatar is favorited, and ask to remove it from list if so
            foreach (string avatarId in favoriteAvatarList)
            {
                if (avatarId == currentApiAvatar.id)
                {
                    favButton.GetComponent<Button>().interactable = false;
                    ShowRemoveAvatarConfirmPopup(avatarId);
                    return;
                }
            }
            //Else, add it to the favorite list
            if (currentApiAvatar.releaseStatus != "public")
            {
                VRCUiPopupManagerUtils.GetVRCUiPopupManager().ShowStandardPopup("Error", "Unable to favorite avatar :<br>This avatar is not public", "Close", () => VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup());
                return;
            }
            favButton.GetComponent<Button>().interactable = false;
            AddAvatar(currentApiAvatar.id);
        }

        private void ShowRemoveAvatarConfirmPopup(string avatarId)
        {
            VRCUiPopupManagerUtils.GetVRCUiPopupManager().ShowStandardPopup("AvatarFav", "Do you really want to unfavorite this avatar ?",
                "Yes", () => {
                    VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
                    RemoveAvatar(avatarId);
                },
                "Cancel", () =>
                {
                    VRCUiPopupManagerUtils.GetVRCUiPopupManager().HideCurrentPopup();
                    favButton.GetComponent<Button>().interactable = true;
                }
            );
        }

        private void LoadAvatars()
        {
            VRCModLogger.Log("[AvatarFav] Loading from cache...");

            handleFreshJSON(File.ReadAllText("AvatarFav.json"), nosave:true, overwrite:false);
            avatarsJSONLoaded = true;
        }

        private void RequestAvatarsFromRPC()
        {
            if (!useNetwork)
                return;
            new Thread(() => VRCModNetworkManager.SendRPC("slaynash.avatarfav.getavatars", "", null, (error) =>
            {
                VRCModLogger.Log("[AvatarFav] Unable to fetch avatars: Server returned " + error);
                if (error.Equals("SERVER_DISCONNECTED"))
                {
                    waitingForServer = true;
                }
            })).Start();
        }

        private void AddAvatar(string id)
        {
            bool changed = true;
            if (!favoriteAvatarList.Contains(id))
            {
                favoriteAvatarList.Add(id);
                changed = true;
            }
            if (!savedFavoriteAvatars.ContainsKey(id))
            {
                savedFavoriteAvatars[id] = new SerializableApiAvatar() { id = id };
                changed = true;
            }
            if (changed)
                Save();
            if (useNetwork && !waitingForServer)
            {
                new Thread(() =>
                {
                    VRCModNetworkManager.SendRPC("slaynash.avatarfav.addavatar", id, null, (error) =>
                    {
                        addError = "Unable to favorite avatar: " + error;
                        favButton.GetComponent<Button>().interactable = true;
                    });
                }).Start();
            }
        }

        internal static void Save()
        {
            if (!avatarsJSONLoaded)
                return;
            var sal = new SimpleAvatarList();
            sal.version = SimpleAvatarList.CURRENT_VERSION;
            sal.avatarIDs = favoriteAvatarList.ToArray();
            using(var s = File.OpenWrite("AvatarFav.json.tmp"))
            using(var w = new StreamWriter(s))
            {
                w.Write(JsonConvert.SerializeObject(sal, Formatting.Indented));
            }
            if (File.Exists("AvatarFav.json"))
                File.Delete("AvatarFav.json");
            File.Move("AvatarFav.json.tmp", "AvatarFav.json");
        }

        private void RemoveAvatar(string id)
        {
            var changed = false;
            if (savedFavoriteAvatars.ContainsKey(id))
            {
                savedFavoriteAvatars.Remove(id);
                changed = true;
            }
            if (favoriteAvatarList.Contains(id))
            {
                favoriteAvatarList.Remove(id);
                changed = true;
            }
            if (changed)
                Save();
            if (useNetwork && !waitingForServer)
            {
                new Thread(() =>
                {
                    VRCModNetworkManager.SendRPC("slaynash.avatarfav.removeavatar", id, null, (error) =>
                    {
                        addError = "Unable to unfavorite avatar: " + error;
                        favButton.GetComponent<Button>().interactable = true;
                    });
                }).Start();
            }
        }
    }
}
