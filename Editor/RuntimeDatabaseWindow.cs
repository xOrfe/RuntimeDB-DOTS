#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using XO.Curve;
using XO.PopUtils;
using XO.RuntimeDatabase;

namespace XO.RuntimeDatabase.Editor
{
    public class RuntimeDatabaseWindow : EditorWindow
    {
        [MenuItem("Tools/Runtime Database Viewer")]
        public static void Open()
        {
            var window = GetWindow<RuntimeDatabaseWindow>();
            window.titleContent = new GUIContent("Runtime Database", EditorGUIUtility.IconContent("d_SaveActive").image);
            window.minSize = new Vector2(750, 480);
            window.Show();
        }

        // String Registry Cache for resolving uint hashes back to human readable strings
        private static readonly Dictionary<uint, string> HashToStringRegistry = new Dictionary<uint, string>();
        private static readonly HashSet<uint> AutoBakeRegistry = new HashSet<uint>();
        private static bool _isRegistryInitialized = false;

        // Header controls (held as references to avoid child queries and ambiguity errors)
        private VisualElement _statusDot;
        private Label _statusLabel;
        private Button _rescanBtn;

        // Stats Card values
        private Label _statTotalTablesVal;
        private Label _statRegistryKeysVal;
        private Label _statCachedPrefabsVal;
        private Label _statActiveStreamsVal;

        // UI Tabs & Views
        private VisualElement _tabHeaderContainer;
        private VisualElement _tabContentContainer;
        private VisualElement _tablesView;
        private VisualElement _spawningView;

        // Split view components
        private ScrollView _tableListScroll;
        private ScrollView _tableDetailsScroll;

        // Spawning View subcomponents
        private ScrollView _prefabCacheScroll;
        private ScrollView _pendingCallsScroll;

        // Tab buttons
        private VisualElement _tabTables;
        private VisualElement _tabSpawning;

        // Play mode overlay banner
        private VisualElement _playModeOverlay;

        // Active State
        private enum ActiveTab { Tables, Spawning }
        private ActiveTab _currentTab = ActiveTab.Tables;

        private uint _selectedTableKey = 0;
        private string _selectedTableType = "";
        private string _searchQuery = "";

        // State tracking to prevent heavy garbage allocations
        private int _lastTableCount = -1;
        private int _lastCacheCount = -1;
        private int _lastCallCount = -1;
        private bool _lastWorldActiveState = false;
        private uint _lastSelectedTableKey = 0;
        private string _lastSelectedTableType = "";
        private string _lastSearchQuery = "";
        private ActiveTab _lastTab = ActiveTab.Tables;

        // Stylistic Tokens (Curated Premium Glassmorphic Look)
        private static readonly Color AccentBlue = new Color(0.2f, 0.6f, 1f, 1f);
        private static readonly Color AccentGreen = new Color(0.18f, 0.8f, 0.44f, 1f);
        private static readonly Color AccentGold = new Color(0.95f, 0.77f, 0.06f, 1f);
        private static readonly Color AccentPurple = new Color(0.6f, 0.35f, 0.9f, 1f);
        private static readonly Color AccentRed = new Color(0.9f, 0.3f, 0.3f, 1f);

        private static readonly Color HeaderBgColor = new Color(0.09f, 0.1f, 0.12f, 1f);
        private static readonly Color DarkBorder = new Color(0.15f, 0.16f, 0.18f, 1f);
        private static readonly Color PanelBgColor = new Color(0.11f, 0.12f, 0.13f, 1f);
        private static readonly Color AltRowBgColor = new Color(0.13f, 0.14f, 0.15f, 0.6f);
        private static readonly Color AccentBorderColor = new Color(0.16f, 0.17f, 0.19f, 1f);
        private static readonly Color ActiveSelectionColor = new Color(0.15f, 0.3f, 0.55f, 0.8f);
        private static readonly Color InactiveTabBg = new Color(0.08f, 0.08f, 0.09f, 1f);

        private void OnEnable()
        {
            if (!_isRegistryInitialized)
            {
                RebuildHashRegistry();
            }
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = new Color(0.08f, 0.09f, 0.1f, 1f);
            root.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);

            // 1. Top Sleek Title Header (Glassmorphic look)
            var header = new VisualElement();
            header.style.backgroundColor = new Color(0.12f, 0.13f, 0.15f, 0.7f);
            header.style.paddingLeft = 15;
            header.style.paddingRight = 15;
            header.style.paddingTop = 12;
            header.style.paddingBottom = 12;
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = DarkBorder;
            header.style.borderTopLeftRadius = 8;
            header.style.borderTopRightRadius = 8;
            header.style.borderBottomLeftRadius = 8;
            header.style.borderBottomRightRadius = 8;
            header.style.marginTop = 8;
            header.style.marginLeft = 8;
            header.style.marginRight = 8;
            root.Add(header);

            // Title Container
            var titleContainer = new VisualElement();
            var title = new Label("🗄️ Runtime Database Viewer");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.color = Color.white;
            titleContainer.Add(title);

            var subtitle = new Label("High-performance unmanaged ECS runtime data diagnostics");
            subtitle.style.fontSize = 9;
            subtitle.style.color = new Color(0.55f, 0.55f, 0.6f, 1f);
            subtitle.style.marginTop = 2;
            titleContainer.Add(subtitle);
            header.Add(titleContainer);

            // Header Right Side: World Indicator & Registry Actions
            var headerRight = new VisualElement();
            headerRight.style.flexDirection = FlexDirection.Row;
            headerRight.style.alignItems = Align.Center;

            // Status Indicator (Dot)
            _statusDot = new VisualElement();
            _statusDot.style.width = 8;
            _statusDot.style.height = 8;
            _statusDot.style.borderTopLeftRadius = 4;
            _statusDot.style.borderTopRightRadius = 4;
            _statusDot.style.borderBottomLeftRadius = 4;
            _statusDot.style.borderBottomRightRadius = 4;
            _statusDot.style.marginRight = 6;
            headerRight.Add(_statusDot);

            _statusLabel = new Label("No World");
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.marginRight = 12;
            headerRight.Add(_statusLabel);

            // Rescan Button
            _rescanBtn = new Button(RebuildHashRegistry);
            _rescanBtn.text = $"🔄 Scan Assets ({HashToStringRegistry.Count} Keys)";
            _rescanBtn.style.backgroundColor = new Color(0.2f, 0.21f, 0.23f, 1f);
            _rescanBtn.style.color = Color.white;
            _rescanBtn.style.borderTopWidth = 0;
            _rescanBtn.style.borderBottomWidth = 0;
            _rescanBtn.style.borderLeftWidth = 0;
            _rescanBtn.style.borderRightWidth = 0;
            _rescanBtn.style.borderTopLeftRadius = 4;
            _rescanBtn.style.borderTopRightRadius = 4;
            _rescanBtn.style.borderBottomLeftRadius = 4;
            _rescanBtn.style.borderBottomRightRadius = 4;
            _rescanBtn.style.paddingLeft = 10;
            _rescanBtn.style.paddingRight = 10;
            _rescanBtn.style.paddingTop = 4;
            _rescanBtn.style.paddingBottom = 4;
            headerRight.Add(_rescanBtn);

            header.Add(headerRight);

            // 1b. Stats Grid Container (Horizontal Cards row)
            var statsRow = new VisualElement();
            statsRow.style.flexDirection = FlexDirection.Row;
            statsRow.style.justifyContent = Justify.SpaceBetween;
            statsRow.style.marginTop = 10;
            statsRow.style.marginLeft = 4;
            statsRow.style.marginRight = 4;

            _statTotalTablesVal = CreateStatCard(statsRow, "Total Tables", "0", AccentBlue, "📂");
            _statRegistryKeysVal = CreateStatCard(statsRow, "Registry Keys", "0", AccentPurple, "🔑");
            _statCachedPrefabsVal = CreateStatCard(statsRow, "Cached Prefabs", "0", AccentGreen, "📦");
            _statActiveStreamsVal = CreateStatCard(statsRow, "Active Streams", "0", AccentGold, "🚀");

            root.Add(statsRow);

            // 2. Navigation Tabs
            _tabHeaderContainer = new VisualElement();
            _tabHeaderContainer.style.flexDirection = FlexDirection.Row;
            _tabHeaderContainer.style.backgroundColor = InactiveTabBg;
            _tabHeaderContainer.style.borderBottomWidth = 1;
            _tabHeaderContainer.style.borderBottomColor = AccentBorderColor;
            _tabHeaderContainer.style.marginTop = 6;
            root.Add(_tabHeaderContainer);

            CreateTabs();

            // 3. Main Content Container
            _tabContentContainer = new VisualElement();
            _tabContentContainer.style.flexGrow = 1;
            root.Add(_tabContentContainer);

            // Build Views
            BuildTablesView();
            BuildSpawningView();

            // Switch to initial tab
            SwitchTab(ActiveTab.Tables);

            // 3b. Build Play Mode Overlay Banner (layered on top)
            BuildPlayModeOverlay();

            // 4. Register Update Loop for live monitoring
            ScheduleUpdate();
        }

        private void ScheduleUpdate()
        {
            // Call update every 150ms
            rootVisualElement.schedule.Execute(OnLiveUpdate).Every(150);
        }

        private void OnLiveUpdate()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var isWorldActive = world != null && world.IsCreated;

            // Update Play Mode Overlay visibility
            if (_playModeOverlay != null)
            {
                _playModeOverlay.style.display = isWorldActive ? DisplayStyle.None : DisplayStyle.Flex;
            }

            // Refresh Status Header
            if (_statusDot != null && _statusLabel != null && _rescanBtn != null)
            {
                if (isWorldActive)
                {
                    _statusDot.style.backgroundColor = AccentGreen;
                    _statusLabel.text = EditorApplication.isPlaying ? $"🟢 DefaultWorld (Play)" : "🟢 DefaultWorld (Editor)";
                    _statusLabel.style.color = AccentGreen;
                }
                else
                {
                    _statusDot.style.backgroundColor = AccentRed;
                    _statusLabel.text = "🔴 No Active ECS World";
                    _statusLabel.style.color = AccentRed;
                }

                _rescanBtn.text = $"🔄 Scan Assets ({HashToStringRegistry.Count} Keys)";
            }

            // Gather dynamic counts
            int currentTableCount = 0;
            int currentCacheCount = 0;
            int currentCallCount = 0;

            if (isWorldActive)
            {
                var em = world.EntityManager;

                // Prefab tables
                var qPrefabs = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<EntityPrefabReference>>());
                if (qPrefabs.HasSingleton<RuntimeTable<EntityPrefabReference>>())
                {
                    var tbl = qPrefabs.GetSingleton<RuntimeTable<EntityPrefabReference>>();
                    if (tbl.Blob.IsCreated) currentTableCount += tbl.Blob.Value.Tables.Length;
                }

                // Float3 splines
                var qFloat3 = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>());
                if (qFloat3.HasSingleton<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>())
                {
                    var tbl = qFloat3.GetSingleton<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>();
                    if (tbl.Blob.IsCreated) currentTableCount += tbl.Blob.Value.Tables.Length;
                }

                // Quaternion splines
                var qQuat = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>());
                if (qQuat.HasSingleton<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>())
                {
                    var tbl = qQuat.GetSingleton<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>();
                    if (tbl.Blob.IsCreated) currentTableCount += tbl.Blob.Value.Tables.Length;
                }

                // Prefab Cache size
                var systemHandle = world.GetExistingSystem<XO.RuntimeDatabase.InstantiateEntitySystem>();
                if (systemHandle != SystemHandle.Null)
                {
                    ref var system = ref world.Unmanaged.GetUnsafeSystemRef<XO.RuntimeDatabase.InstantiateEntitySystem>(systemHandle);
                    if (system.PrefabCache.IsCreated)
                    {
                        currentCacheCount = system.PrefabCache.Count;
                    }
                }

                // Spawning streams size
                var qCalls = em.CreateEntityQuery(ComponentType.ReadOnly<InstantiateEntityPrefabCall>());
                currentCallCount = qCalls.CalculateEntityCount();
            }

            // Update stats cards text
            if (_statTotalTablesVal != null) _statTotalTablesVal.text = currentTableCount.ToString();
            if (_statRegistryKeysVal != null) _statRegistryKeysVal.text = HashToStringRegistry.Count.ToString();
            if (_statCachedPrefabsVal != null) _statCachedPrefabsVal.text = currentCacheCount.ToString();
            if (_statActiveStreamsVal != null) _statActiveStreamsVal.text = currentCallCount.ToString();

            // High-performance State Check
            bool stateChanged = isWorldActive != _lastWorldActiveState
                || _currentTab != _lastTab
                || _selectedTableKey != _lastSelectedTableKey
                || _selectedTableType != _lastSelectedTableType
                || _searchQuery != _lastSearchQuery;

            bool countChanged = currentTableCount != _lastTableCount
                || currentCacheCount != _lastCacheCount
                || currentCallCount != _lastCallCount;

            _lastWorldActiveState = isWorldActive;
            _lastTab = _currentTab;
            _lastSelectedTableKey = _selectedTableKey;
            _lastSelectedTableType = _selectedTableType;
            _lastSearchQuery = _searchQuery;
            _lastTableCount = currentTableCount;
            _lastCacheCount = currentCacheCount;
            _lastCallCount = currentCallCount;

            // Only rebuild layout scrolls if count or selection state changed!
            if (stateChanged || countChanged)
            {
                if (_currentTab == ActiveTab.Tables)
                {
                    RefreshTableList(_tableListScroll);
                    RefreshTableDetails(_tableDetailsScroll);
                }
                else if (_currentTab == ActiveTab.Spawning)
                {
                    RefreshPrefabCache(_prefabCacheScroll);
                    RefreshPendingCalls(_pendingCallsScroll);
                }
            }
        }

        private void CreateTabs()
        {
            _tabHeaderContainer.Clear();

            _tabTables = CreateTabButton("🗄️ Database Tables", ActiveTab.Tables);
            _tabSpawning = CreateTabButton("🚀 Spawn & Prefab Cache", ActiveTab.Spawning);

            _tabHeaderContainer.Add(_tabTables);
            _tabHeaderContainer.Add(_tabSpawning);
        }

        private VisualElement CreateTabButton(string labelText, ActiveTab tab)
        {
            var tabBtn = new VisualElement();
            tabBtn.style.flexGrow = 1;
            tabBtn.style.alignItems = Align.Center;
            tabBtn.style.justifyContent = Justify.Center;
            tabBtn.style.paddingTop = 10;
            tabBtn.style.paddingBottom = 10;

            var label = new Label(labelText);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 12;
            tabBtn.Add(label);

            bool isActive = _currentTab == tab;

            if (isActive)
            {
                tabBtn.style.backgroundColor = new Color(0.14f, 0.16f, 0.2f, 0.7f);
                label.style.color = Color.white;
                tabBtn.style.borderBottomWidth = 2;
                tabBtn.style.borderBottomColor = AccentBlue;
            }
            else
            {
                tabBtn.style.backgroundColor = Color.clear;
                label.style.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                tabBtn.style.borderBottomWidth = 1;
                tabBtn.style.borderBottomColor = AccentBorderColor;
            }

            // Register mouse click
            tabBtn.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SwitchTab(tab);
                }
            });

            // Register hover state
            tabBtn.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (_currentTab != tab)
                {
                    tabBtn.style.backgroundColor = new Color(0.13f, 0.14f, 0.15f, 0.5f);
                    label.style.color = Color.white;
                    tabBtn.style.borderBottomWidth = 1;
                    tabBtn.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 1f);
                }
            });

            tabBtn.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (_currentTab != tab)
                {
                    tabBtn.style.backgroundColor = Color.clear;
                    label.style.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                    tabBtn.style.borderBottomWidth = 1;
                    tabBtn.style.borderBottomColor = AccentBorderColor;
                }
            });

            return tabBtn;
        }

        private void SwitchTab(ActiveTab tab)
        {
            _currentTab = tab;
            CreateTabs(); // Redraw tab headers with active state

            _tabContentContainer.Clear();
            if (tab == ActiveTab.Tables)
            {
                _tabContentContainer.Add(_tablesView);
                RefreshTableList(_tableListScroll);
                RefreshTableDetails(_tableDetailsScroll);
            }
            else
            {
                _tabContentContainer.Add(_spawningView);
                RefreshPrefabCache(_prefabCacheScroll);
                RefreshPendingCalls(_pendingCallsScroll);
            }
        }

        private Label CreateStatCard(VisualElement parent, string title, string initialValue, Color accentColor, string emoji)
        {
            var card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.flexShrink = 1;
            card.style.backgroundColor = new Color(0.13f, 0.14f, 0.16f, 0.6f);
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = AccentBorderColor;
            card.style.borderBottomColor = AccentBorderColor;
            card.style.borderLeftColor = AccentBorderColor;
            card.style.borderRightColor = AccentBorderColor;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.marginRight = 4;
            card.style.marginLeft = 4;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var emojiLabel = new Label(emoji);
            emojiLabel.style.marginRight = 4;
            emojiLabel.style.fontSize = 10;
            titleRow.Add(emojiLabel);

            var titleLabel = new Label(title.ToUpper());
            titleLabel.style.fontSize = 8;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.6f, 0.6f, 0.65f, 1f);
            titleRow.Add(titleLabel);

            card.Add(titleRow);

            var valLabel = new Label(initialValue);
            valLabel.style.fontSize = 16;
            valLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valLabel.style.color = accentColor;
            valLabel.style.marginTop = 4;
            card.Add(valLabel);

            parent.Add(card);
            return valLabel;
        }

        private void BuildPlayModeOverlay()
        {
            _playModeOverlay = new VisualElement();
            _playModeOverlay.style.position = Position.Absolute;
            _playModeOverlay.style.top = 110; // Below header and stats grid
            _playModeOverlay.style.bottom = 0;
            _playModeOverlay.style.left = 0;
            _playModeOverlay.style.right = 0;
            _playModeOverlay.style.backgroundColor = new Color(0.08f, 0.08f, 0.09f, 0.95f);
            _playModeOverlay.style.justifyContent = Justify.Center;
            _playModeOverlay.style.alignItems = Align.Center;

            var card = new VisualElement();
            card.style.width = 440;
            card.style.backgroundColor = new Color(0.14f, 0.15f, 0.17f, 0.85f);
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = AccentBorderColor;
            card.style.borderBottomColor = AccentBorderColor;
            card.style.borderLeftColor = AccentBorderColor;
            card.style.borderRightColor = AccentBorderColor;
            card.style.borderTopLeftRadius = 8;
            card.style.borderTopRightRadius = 8;
            card.style.borderBottomLeftRadius = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.paddingTop = 20;
            card.style.paddingBottom = 20;
            card.style.paddingLeft = 25;
            card.style.paddingRight = 25;
            card.style.alignItems = Align.Center;

            var title = new Label("🚫 NO ACTIVE ECS WORLD FOUND");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            title.style.color = AccentRed;
            title.style.marginBottom = 10;
            card.Add(title);

            var desc = new Label("Enter Play Mode in the Unity Editor to automatically bake the runtime database tables, start prefab streaming systems, and view unmanaged caches.");
            desc.style.fontSize = 10;
            desc.style.color = new Color(0.7f, 0.7f, 0.75f, 1f);
            desc.style.unityTextAlign = TextAnchor.MiddleCenter;
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginBottom = 15;
            card.Add(desc);

            var scanBtn = new Button(RebuildHashRegistry);
            scanBtn.text = $"🔄 Scan Static Mappings ({HashToStringRegistry.Count} Keys)";
            scanBtn.style.backgroundColor = AccentBlue;
            scanBtn.style.color = Color.white;
            scanBtn.style.borderTopLeftRadius = 4;
            scanBtn.style.borderTopRightRadius = 4;
            scanBtn.style.borderBottomLeftRadius = 4;
            scanBtn.style.borderBottomRightRadius = 4;
            scanBtn.style.paddingTop = 6;
            scanBtn.style.paddingBottom = 6;
            scanBtn.style.paddingLeft = 14;
            scanBtn.style.paddingRight = 14;
            scanBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            scanBtn.style.borderTopWidth = 0;
            scanBtn.style.borderBottomWidth = 0;
            scanBtn.style.borderLeftWidth = 0;
            scanBtn.style.borderRightWidth = 0;
            card.Add(scanBtn);

            _playModeOverlay.Add(card);
            rootVisualElement.Add(_playModeOverlay);
        }

        #region TAB 1: TABLES VIEW
        private void BuildTablesView()
        {
            _tablesView = new VisualElement();
            _tablesView.style.flexGrow = 1;

            // Two Pane Split View
            var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1;
            _tablesView.Add(splitView);

            // Left Pane (List of Tables)
            var leftPane = new VisualElement();
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.style.backgroundColor = PanelBgColor;
            leftPane.style.paddingTop = 10;
            leftPane.style.paddingLeft = 5;
            leftPane.style.paddingRight = 5;
            splitView.Add(leftPane);

            _tableListScroll = new ScrollView(ScrollViewMode.Vertical);
            _tableListScroll.style.flexGrow = 1;
            leftPane.Add(_tableListScroll);

            // Right Pane (Selected Table Details)
            var rightPane = new VisualElement();
            rightPane.style.flexDirection = FlexDirection.Column;
            rightPane.style.paddingTop = 10;
            rightPane.style.paddingLeft = 10;
            rightPane.style.paddingRight = 10;
            splitView.Add(rightPane);

            // Search Bar & Details Header
            var searchHeader = new VisualElement();
            searchHeader.style.flexDirection = FlexDirection.Row;
            searchHeader.style.justifyContent = Justify.SpaceBetween;
            searchHeader.style.alignItems = Align.Center;
            searchHeader.style.marginBottom = 10;
            rightPane.Add(searchHeader);

            var searchLabel = new Label("🔍 Filter entries:");
            searchLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            searchLabel.style.marginRight = 10;
            searchHeader.Add(searchLabel);

            var searchField = new ToolbarSearchField();
            searchField.style.flexGrow = 1;
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue;
                RefreshTableDetails(_tableDetailsScroll);
            });
            searchHeader.Add(searchField);

            _tableDetailsScroll = new ScrollView(ScrollViewMode.Vertical);
            _tableDetailsScroll.style.flexGrow = 1;
            rightPane.Add(_tableDetailsScroll);
        }

        private void RefreshTableList(VisualElement container)
        {
            container.Clear();

            var header = new Label("Loaded Database Tables");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 13;
            header.style.paddingBottom = 8;
            header.style.color = Color.white;
            container.Add(header);

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                var lbl = new Label("No Active ECS World found.\nEnter Play Mode to bake database.");
                lbl.style.color = Color.gray;
                lbl.style.paddingLeft = 10;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                container.Add(lbl);
                return;
            }

            var em = world.EntityManager;

            // Group 1: Prefab Tables
            container.Add(CreateGroupHeader("📦 Prefab Tables"));
            bool hasPrefabs = false;
            var qPrefabs = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<EntityPrefabReference>>());
            if (qPrefabs.HasSingleton<RuntimeTable<EntityPrefabReference>>())
            {
                var tbl = qPrefabs.GetSingleton<RuntimeTable<EntityPrefabReference>>();
                if (tbl.Blob.IsCreated)
                {
                    ref var root = ref tbl.Blob.Value;
                    for (int i = 0; i < root.Tables.Length; i++)
                    {
                        uint key = root.Tables[i].TableKey;
                        int count = root.Tables[i].Count;
                        container.Add(CreateTableButton(key, "Prefab", count));
                        hasPrefabs = true;
                    }
                }
            }
            if (!hasPrefabs)
            {
                var lbl = new Label("  No baked prefab tables");
                lbl.style.color = Color.gray;
                lbl.style.fontSize = 11;
                container.Add(lbl);
            }

            // Group 2: Float3 Spline Tables
            container.Add(CreateGroupHeader("〰️ Float3 Splines"));
            bool hasFloat3 = false;
            var qFloat3 = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>());
            if (qFloat3.HasSingleton<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>())
            {
                var tbl = qFloat3.GetSingleton<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>();
                if (tbl.Blob.IsCreated)
                {
                    ref var root = ref tbl.Blob.Value;
                    for (int i = 0; i < root.Tables.Length; i++)
                    {
                        uint key = root.Tables[i].TableKey;
                        int count = root.Tables[i].Count;
                        container.Add(CreateTableButton(key, "Float3Spline", count));
                        hasFloat3 = true;
                    }
                }
            }
            if (!hasFloat3)
            {
                var lbl = new Label("  No baked float3 spline tables");
                lbl.style.color = Color.gray;
                lbl.style.fontSize = 11;
                container.Add(lbl);
            }

            // Group 3: Quaternion Spline Tables
            container.Add(CreateGroupHeader("🔄 Quaternion Splines"));
            bool hasQuat = false;
            var qQuat = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>());
            if (qQuat.HasSingleton<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>())
            {
                var tbl = qQuat.GetSingleton<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>();
                if (tbl.Blob.IsCreated)
                {
                    ref var root = ref tbl.Blob.Value;
                    for (int i = 0; i < root.Tables.Length; i++)
                    {
                        uint key = root.Tables[i].TableKey;
                        int count = root.Tables[i].Count;
                        container.Add(CreateTableButton(key, "QuaternionSpline", count));
                        hasQuat = true;
                    }
                }
            }
            if (!hasQuat)
            {
                var lbl = new Label("  No baked quaternion spline tables");
                lbl.style.color = Color.gray;
                lbl.style.fontSize = 11;
                container.Add(lbl);
            }
        }

        private VisualElement CreateGroupHeader(string title)
        {
            var lbl = new Label(title);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.fontSize = 11;
            lbl.style.marginTop = 12;
            lbl.style.marginBottom = 4;
            lbl.style.paddingLeft = 4;
            lbl.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            return lbl;
        }

        private Button CreateTableButton(uint tableKey, string type, int elementCount)
        {
            var tableName = ResolveHash(tableKey);
            var isSelected = _selectedTableKey == tableKey && _selectedTableType == type;

            var btn = new Button(() =>
            {
                _selectedTableKey = tableKey;
                _selectedTableType = type;
                RefreshTableDetails(_tableDetailsScroll);
                RefreshTableList(_tableListScroll);
            });

            var autoBakeTag = AutoBakeRegistry.Contains(tableKey) ? " ⚡" : "";
            btn.text = $"{tableName}{autoBakeTag}  [{elementCount}]";
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.paddingTop = 4;
            btn.style.paddingBottom = 4;
            btn.style.paddingLeft = 8;
            btn.style.marginBottom = 2;

            if (isSelected)
            {
                btn.style.backgroundColor = ActiveSelectionColor;
                btn.style.color = Color.white;
            }
            else
            {
                btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.4f);
                btn.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            }

            btn.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (!isSelected) btn.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);
            });
            btn.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (!isSelected) btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.4f);
            });

            return btn;
        }

        private void RefreshTableDetails(VisualElement container)
        {
            container.Clear();

            if (_selectedTableKey == 0)
            {
                var welcome = new Label("👈 Select a loaded table on the left to inspect its contents.");
                welcome.style.color = Color.gray;
                welcome.style.fontSize = 13;
                welcome.style.marginTop = 40;
                welcome.style.unityTextAlign = TextAnchor.MiddleCenter;
                container.Add(welcome);
                return;
            }

            var tableName = ResolveHash(_selectedTableKey);
            var autoBakeNotice = AutoBakeRegistry.Contains(_selectedTableKey) ? "  [⚡ Auto-Baked]" : "";
            var header = new Label($"Table: {tableName}{autoBakeNotice}  ({_selectedTableType})");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 14;
            header.style.color = Color.white;
            header.style.marginBottom = 10;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = AccentBorderColor;
            header.style.paddingBottom = 5;
            container.Add(header);

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            // Draw Entries depending on selected table type
            if (_selectedTableType == "Prefab")
            {
                var qPrefabs = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<EntityPrefabReference>>());
                if (qPrefabs.HasSingleton<RuntimeTable<EntityPrefabReference>>())
                {
                    var tbl = qPrefabs.GetSingleton<RuntimeTable<EntityPrefabReference>>();
                    if (tbl.Blob.IsCreated)
                    {
                        ref var root = ref tbl.Blob.Value;
                        if (TryFindTableMetadata(ref root.Tables, _selectedTableKey, out var meta))
                        {
                            DrawPrefabEntries(container, ref root, meta.StartIndex, meta.Count);
                        }
                    }
                }
            }
            else if (_selectedTableType == "Float3Spline")
            {
                var qFloat3 = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>());
                if (qFloat3.HasSingleton<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>())
                {
                    var tbl = qFloat3.GetSingleton<RuntimeTable<BlobAssetReference<SplineBlob<float3>>>>();
                    if (tbl.Blob.IsCreated)
                    {
                        ref var root = ref tbl.Blob.Value;
                        if (TryFindTableMetadata(ref root.Tables, _selectedTableKey, out var meta))
                        {
                            DrawSplineEntries(container, ref root, meta.StartIndex, meta.Count);
                        }
                    }
                }
            }
            else if (_selectedTableType == "QuaternionSpline")
            {
                var qQuat = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>());
                if (qQuat.HasSingleton<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>())
                {
                    var tbl = qQuat.GetSingleton<RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>>();
                    if (tbl.Blob.IsCreated)
                    {
                        ref var root = ref tbl.Blob.Value;
                        if (TryFindTableMetadata(ref root.Tables, _selectedTableKey, out var meta))
                        {
                            DrawSplineEntries(container, ref root, meta.StartIndex, meta.Count);
                        }
                    }
                }
            }
        }

        private bool TryFindTableMetadata(ref BlobArray<TableMetadata> tables, uint key, out TableMetadata metadata)
        {
            int low = 0;
            int high = tables.Length - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                uint midKey = tables[mid].TableKey;
                if (midKey == key)
                {
                    metadata = tables[mid];
                    return true;
                }
                else if (midKey < key)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            metadata = default;
            return false;
        }

        private void DrawPrefabEntries(VisualElement container, ref RuntimeDatabaseBlob<EntityPrefabReference> blob, int startIndex, int count)
        {
            var matchCount = 0;
            for (int i = 0; i < count; i++)
            {
                ref var entry = ref blob.Entries[startIndex + i];
                uint keyHash = entry.ObjectKey;
                var itemName = ResolveHash(keyHash);

                // Search Filter
                if (!string.IsNullOrEmpty(_searchQuery) &&
                    itemName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    keyHash.ToString("X").IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                EntityPrefabReference prefRef = entry.Value;

                var row = CreateTableRow(i % 2 == 0);

                var nameLbl = new Label($"🔑  {itemName}");
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.flexGrow = 1;
                row.Add(nameLbl);

                var hashLbl = new Label($"0x{keyHash:X8}");
                hashLbl.style.color = Color.gray;
                hashLbl.style.fontSize = 11;
                hashLbl.style.marginRight = 20;
                row.Add(hashLbl);

                var valLbl = new Label($"AssetGUID: {prefRef.AssetGUID}");
                valLbl.style.color = new Color(0.4f, 0.8f, 0.4f, 1f);
                valLbl.style.marginRight = 10;
                row.Add(valLbl);

                container.Add(row);
                matchCount++;
            }

            if (matchCount == 0)
            {
                container.Add(new Label("No entries found matching search query.") { style = { color = Color.gray, marginTop = 15 } });
            }
        }

        private void DrawSplineEntries<T>(VisualElement container, ref RuntimeDatabaseBlob<BlobAssetReference<SplineBlob<T>>> blob, int startIndex, int count) where T : unmanaged
        {
            var matchCount = 0;
            for (int i = 0; i < count; i++)
            {
                ref var entry = ref blob.Entries[startIndex + i];
                uint keyHash = entry.ObjectKey;
                var itemName = ResolveHash(keyHash);

                // Search Filter
                if (!string.IsNullOrEmpty(_searchQuery) &&
                    itemName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    keyHash.ToString("X").IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var splineRef = entry.Value;

                var row = CreateTableRow(i % 2 == 0);

                var nameLbl = new Label($"🔑  {itemName}");
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.flexGrow = 1;
                row.Add(nameLbl);

                var hashLbl = new Label($"0x{keyHash:X8}");
                hashLbl.style.color = Color.gray;
                hashLbl.style.fontSize = 11;
                hashLbl.style.marginRight = 20;
                row.Add(hashLbl);

                if (splineRef.IsCreated)
                {
                    ref var spline = ref splineRef.Value;
                    var closedStr = spline.IsClosed ? "Closed" : "Open";
                    var valLbl = new Label($"{spline.SplineType} Spline ({closedStr}), {spline.Elements.Length} Knots, Len: {spline.TotalWeight:F2}m");
                    valLbl.style.color = new Color(0.4f, 0.7f, 0.9f, 1f);
                    valLbl.style.marginRight = 10;
                    row.Add(valLbl);
                }
                else
                {
                    var valLbl = new Label("Null Spline Reference");
                    valLbl.style.color = Color.red;
                    valLbl.style.marginRight = 10;
                    row.Add(valLbl);
                }

                container.Add(row);
                matchCount++;
            }

            if (matchCount == 0)
            {
                container.Add(new Label("No entries found matching search query.") { style = { color = Color.gray, marginTop = 15 } });
            }
        }

        private VisualElement CreateTableRow(bool alternateBg)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            row.style.alignItems = Align.Center;

            if (alternateBg)
            {
                row.style.backgroundColor = AltRowBgColor;
            }

            return row;
        }
        #endregion

        #region TAB 2: SPAWNING & PREFAB CACHE VIEW
        private void BuildSpawningView()
        {
            _spawningView = new VisualElement();
            _spawningView.style.flexGrow = 1;
            _spawningView.style.paddingLeft = 12;
            _spawningView.style.paddingRight = 12;
            _spawningView.style.paddingTop = 10;

            // Panel 1: Prefab Cache
            var cacheHeader = new Label("📦 Cached Prefab Entities (InstantiateEntitySystem)");
            cacheHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            cacheHeader.style.fontSize = 13;
            cacheHeader.style.color = Color.white;
            cacheHeader.style.marginBottom = 5;
            _spawningView.Add(cacheHeader);

            var cacheContainer = new VisualElement();
            cacheContainer.style.height = 180;
            cacheContainer.style.backgroundColor = PanelBgColor;
            cacheContainer.style.borderTopWidth = 1;
            cacheContainer.style.borderBottomWidth = 1;
            cacheContainer.style.borderLeftWidth = 1;
            cacheContainer.style.borderRightWidth = 1;
            cacheContainer.style.borderTopColor = AccentBorderColor;
            cacheContainer.style.borderBottomColor = AccentBorderColor;
            cacheContainer.style.borderLeftColor = AccentBorderColor;
            cacheContainer.style.borderRightColor = AccentBorderColor;
            cacheContainer.style.borderTopLeftRadius = 4;
            cacheContainer.style.borderTopRightRadius = 4;
            cacheContainer.style.borderBottomLeftRadius = 4;
            cacheContainer.style.borderBottomRightRadius = 4;
            cacheContainer.style.marginBottom = 15;
            _spawningView.Add(cacheContainer);

            _prefabCacheScroll = new ScrollView(ScrollViewMode.Vertical);
            _prefabCacheScroll.style.flexGrow = 1;
            _prefabCacheScroll.style.paddingLeft = 8;
            _prefabCacheScroll.style.paddingRight = 8;
            _prefabCacheScroll.style.paddingTop = 6;
            cacheContainer.Add(_prefabCacheScroll);

            // Panel 2: In-Progress Spawn Calls
            var callsHeader = new Label("🚀 In-Progress Spawning Calls (Active Prefab Streaming)");
            callsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            callsHeader.style.fontSize = 13;
            callsHeader.style.color = Color.white;
            callsHeader.style.marginBottom = 5;
            _spawningView.Add(callsHeader);

            var callsContainer = new VisualElement();
            callsContainer.style.flexGrow = 1;
            callsContainer.style.backgroundColor = PanelBgColor;
            callsContainer.style.borderTopWidth = 1;
            callsContainer.style.borderBottomWidth = 1;
            callsContainer.style.borderLeftWidth = 1;
            callsContainer.style.borderRightWidth = 1;
            callsContainer.style.borderTopColor = AccentBorderColor;
            callsContainer.style.borderBottomColor = AccentBorderColor;
            callsContainer.style.borderLeftColor = AccentBorderColor;
            callsContainer.style.borderRightColor = AccentBorderColor;
            callsContainer.style.borderTopLeftRadius = 4;
            callsContainer.style.borderTopRightRadius = 4;
            callsContainer.style.borderBottomLeftRadius = 4;
            callsContainer.style.borderBottomRightRadius = 4;
            callsContainer.style.marginBottom = 10;
            _spawningView.Add(callsContainer);

            _pendingCallsScroll = new ScrollView(ScrollViewMode.Vertical);
            _pendingCallsScroll.style.flexGrow = 1;
            _pendingCallsScroll.style.paddingLeft = 8;
            _pendingCallsScroll.style.paddingRight = 8;
            _pendingCallsScroll.style.paddingTop = 6;
            callsContainer.Add(_pendingCallsScroll);
        }

        private void RefreshPrefabCache(VisualElement container)
        {
            container.Clear();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                var lbl = new Label("No Active World - Enter Play Mode to inspect cache.");
                lbl.style.color = Color.gray;
                container.Add(lbl);
                return;
            }

            var systemHandle = world.GetExistingSystem<XO.RuntimeDatabase.InstantiateEntitySystem>();
            if (systemHandle == SystemHandle.Null)
            {
                var lbl = new Label("InstantiateEntitySystem not active in this World.");
                lbl.style.color = Color.gray;
                container.Add(lbl);
                return;
            }

            ref var system = ref world.Unmanaged.GetUnsafeSystemRef<XO.RuntimeDatabase.InstantiateEntitySystem>(systemHandle);
            var cache = system.PrefabCache;

            if (!cache.IsCreated || cache.Count == 0)
            {
                var lbl = new Label("Prefab cache is currently empty.\nPrefabs are loaded asynchronously and cached upon their first instantiation call.");
                lbl.style.color = Color.gray;
                lbl.style.fontSize = 12;
                lbl.style.marginTop = 20;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                container.Add(lbl);
                return;
            }

            var idx = 0;
            foreach (var kvp in cache)
            {
                ulong cacheKey = kvp.Key;
                Entity cachedPrefabRoot = kvp.Value;

                // Unpack key
                uint tableKey = (uint)(cacheKey >> 32);
                uint itemKey = (uint)(cacheKey & 0xFFFFFFFF);

                var tableName = ResolveHash(tableKey);
                var itemName = ResolveHash(itemKey);

                var row = CreateTableRow(idx % 2 == 0);

                var label = new Label($"📦  <b>{tableName}</b> / {itemName}");
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                label.style.flexGrow = 1;
                row.Add(label);

                var hexLabel = new Label($"0x{cacheKey:X16}");
                hexLabel.style.color = new Color(0.5f, 0.5f, 0.55f, 1f);
                hexLabel.style.fontSize = 10;
                hexLabel.style.marginRight = 12;
                row.Add(hexLabel);

                // Clickable entity badge — selects entity in Inspector
                var entBadge = CreateEntityBadge(world, cachedPrefabRoot);
                row.Add(entBadge);

                container.Add(row);
                idx++;
            }
        }

        private void RefreshPendingCalls(VisualElement container)
        {
            container.Clear();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<InstantiateEntityPrefabCall>());

            if (query.IsEmpty)
            {
                var lbl = new Label("No active spawning / prefab loading streams.\nSpawn entities in your game to see them here.");
                lbl.style.color = Color.gray;
                lbl.style.fontSize = 12;
                lbl.style.marginTop = 30;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                container.Add(lbl);
                return;
            }

            var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var ent = entities[i];
                var call = em.GetComponentData<InstantiateEntityPrefabCall>(ent);

                var tableName = ResolveHash(call.TableKey);
                var itemName = ResolveHash(call.Key);

                var row = CreateTableRow(i % 2 == 0);

                var label = new Label($"⚡  <b>{tableName}</b> / {itemName}  x {call.InstantiateCount}");
                label.style.flexGrow = 1;
                row.Add(label);

                // Clickable entity badge for the call entity
                var entBadge = CreateEntityBadge(world, ent);
                row.Add(entBadge);

                if (call.Parent != Entity.Null)
                {
                    var parentBadge = CreateEntityBadge(world, call.Parent, $"Parent {call.Parent.Index}:{call.Parent.Version}");
                    row.Add(parentBadge);
                }

                // Check load status: if has component RequestEntityPrefabLoaded
                var isLoading = em.HasComponent<RequestEntityPrefabLoaded>(ent);
                var statusLbl = new Label(isLoading ? "🟡 STREAMING" : "🟢 QUEUED / BAKING");
                statusLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                statusLbl.style.fontSize = 10;
                statusLbl.style.color = isLoading ? new Color(0.9f, 0.7f, 0.2f, 1f) : new Color(0.3f, 0.8f, 0.3f, 1f);
                statusLbl.style.paddingLeft = 6;
                statusLbl.style.paddingRight = 6;
                statusLbl.style.paddingTop = 2;
                statusLbl.style.paddingBottom = 2;
                statusLbl.style.backgroundColor = isLoading ? new Color(0.3f, 0.2f, 0.05f, 0.8f) : new Color(0.05f, 0.2f, 0.05f, 0.8f);
                statusLbl.style.borderTopLeftRadius = 3;
                statusLbl.style.borderTopRightRadius = 3;
                statusLbl.style.borderBottomLeftRadius = 3;
                statusLbl.style.borderBottomRightRadius = 3;
                row.Add(statusLbl);

                container.Add(row);
            }

            entities.Dispose();
        }
        #endregion

        #region SCAN & HASH REGISTRY UTILITIES
        private static void RebuildHashRegistry()
        {
            HashToStringRegistry.Clear();
            AutoBakeRegistry.Clear();
            _isRegistryInitialized = true;

            // 1. Scan for loaded assets using asset scanner
            var guids = AssetDatabase.FindAssets("t:XO.RuntimeDatabase.Authoring.RuntimeTableSoBase");
            if (guids == null || guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets("t:RuntimeTableSoBase");
            }

            if (guids == null) return;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<XO.RuntimeDatabase.Authoring.RuntimeTableSoBase>(path);
                if (asset != null)
                {
                    // Map Table Name
                    var tableName = string.IsNullOrEmpty(asset.tableName) ? asset.name : asset.tableName;
                    var tableHash = Pop.Hash32(tableName);
                    HashToStringRegistry[tableHash] = tableName;
                    if (asset is XO.RuntimeDatabase.Authoring.IRuntimePrefabTable)
                    {
                        AutoBakeRegistry.Add(tableHash);
                    }

                    // Use Reflection to read entries in generic subclasses
                    var type = asset.GetType();
                    var dataField = type.GetField("data", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (dataField != null)
                    {
                        var dataArray = dataField.GetValue(asset) as System.Collections.IEnumerable;
                        if (dataArray != null)
                        {
                            foreach (var item in dataArray)
                            {
                                if (item == null) continue;
                                var itemType = item.GetType();
                                var keyField = itemType.GetField("key", BindingFlags.Public | BindingFlags.Instance);
                                if (keyField != null)
                                {
                                    var keyStr = keyField.GetValue(item) as string;
                                    if (!string.IsNullOrEmpty(keyStr))
                                    {
                                        var keyHash = Pop.Hash32(keyStr);
                                        HashToStringRegistry[keyHash] = keyStr;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Debug.Log($"[Runtime Database Viewer] String Hash Resolution Registry scanned successfully. Loaded {HashToStringRegistry.Count} string mappings from scriptable objects.");
        }

        private static string ResolveHash(uint hash)
        {
            if (HashToStringRegistry.TryGetValue(hash, out var name))
            {
                return name;
            }
            return $"0x{hash:X8} ({hash})";
        }

        private static void PingEntity(World world, Entity entity)
        {
            try
            {
                var proxyType = Type.GetType("Unity.Entities.Editor.EntitySelectionProxy, Unity.Entities.Editor");
                if (proxyType != null)
                {
                    var selectMethod = proxyType.GetMethod("SelectEntity",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (selectMethod != null)
                    {
                        selectMethod.Invoke(null, new object[] { world, entity });
                        return;
                    }
                }
                Debug.LogWarning("[Runtime Database Viewer] EntitySelectionProxy.SelectEntity not found. Is the Entities package installed?");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Runtime Database Viewer] PingEntity failed: {ex.Message}");
            }
        }

        private static VisualElement CreateEntityBadge(World world, Entity entity, string label = null)
        {
            var text = label ?? $"{entity.Index}:{entity.Version}";
            var badge = new Label($"🔹 {text}");
            badge.style.color = AccentBlue;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.fontSize = 10;
            badge.style.paddingLeft = 5;
            badge.style.paddingRight = 5;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.borderTopWidth = 1;
            badge.style.borderBottomWidth = 1;
            badge.style.borderLeftWidth = 1;
            badge.style.borderRightWidth = 1;
            badge.style.borderTopColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
            badge.style.borderBottomColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
            badge.style.borderLeftColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
            badge.style.borderRightColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
            badge.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.08f);
            badge.style.marginRight = 8;

            badge.RegisterCallback<MouseEnterEvent>(evt =>
            {
                badge.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.22f);
                badge.style.borderTopColor = AccentBlue;
                badge.style.borderBottomColor = AccentBlue;
                badge.style.borderLeftColor = AccentBlue;
                badge.style.borderRightColor = AccentBlue;
            });
            badge.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                badge.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.08f);
                badge.style.borderTopColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
                badge.style.borderBottomColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
                badge.style.borderLeftColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
                badge.style.borderRightColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f);
            });
            badge.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                    PingEntity(world, entity);
            });

            return badge;
        }
        #endregion
    }
}
#endif
