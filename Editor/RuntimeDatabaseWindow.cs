#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using XO.RuntimeDatabase.Authoring;

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

        private enum ActiveTab { Tables, Spawning }

        private struct DiscoveredTableInfo
        {
            public Type ValueType;
            public string TypeDisplayName;
            public uint TableKey;
            public int EntryCount;
        }

        private struct EntryRowData
        {
            public uint ObjectKey;
            public string ValueText;
            public Color ValueColor;
        }

        private readonly Dictionary<uint, string> _hashToStringRegistry = new();
        private readonly HashSet<uint> _autoBakeRegistry = new();
        private readonly List<Type> _cachedTableValueTypes = new();
        private bool _isRegistryInitialized;
        private bool _isTableTypesDiscovered;

        private MethodInfo _collectTablesGenericMethod;
        private MethodInfo _collectEntriesGenericMethod;

        // UI References
        private VisualElement _statusDot;
        private Label _statusLabel;
        private Button _rescanBtn;
        private Label _statTotalTablesVal;
        private Label _statRegistryKeysVal;
        private Label _statCachedPrefabsVal;
        private Label _statActiveStreamsVal;
        private VisualElement _tabHeaderContainer;
        private VisualElement _tabContentContainer;
        private VisualElement _tablesView;
        private VisualElement _spawningView;
        private ScrollView _tableListScroll;
        private ScrollView _tableDetailsScroll;
        private ScrollView _prefabCacheScroll;
        private ScrollView _pendingCallsScroll;
        private VisualElement _playModeOverlay;

        // State
        private ActiveTab _currentTab = ActiveTab.Tables;
        private uint _selectedTableKey;
        private Type _selectedValueType;
        private string _searchQuery = "";

        // Change-tracking to avoid unnecessary rebuilds
        private int _lastTableCount = -1;
        private int _lastCacheCount = -1;
        private int _lastCallCount = -1;
        private bool _lastWorldActiveState;
        private uint _lastSelectedTableKey;
        private Type _lastSelectedValueType;
        private string _lastSearchQuery = "";
        private ActiveTab _lastTab = ActiveTab.Tables;

        // Palette
        private static readonly Color AccentBlue = new(0.2f, 0.6f, 1f, 1f);
        private static readonly Color AccentGreen = new(0.18f, 0.8f, 0.44f, 1f);
        private static readonly Color AccentGold = new(0.95f, 0.77f, 0.06f, 1f);
        private static readonly Color AccentPurple = new(0.6f, 0.35f, 0.9f, 1f);
        private static readonly Color AccentRed = new(0.9f, 0.3f, 0.3f, 1f);

        private static readonly Color DarkBorder = new(0.15f, 0.16f, 0.18f, 1f);
        private static readonly Color PanelBgColor = new(0.11f, 0.12f, 0.13f, 1f);
        private static readonly Color AltRowBgColor = new(0.13f, 0.14f, 0.15f, 0.6f);
        private static readonly Color AccentBorderColor = new(0.16f, 0.17f, 0.19f, 1f);
        private static readonly Color ActiveSelectionColor = new(0.15f, 0.3f, 0.55f, 0.8f);
        private static readonly Color InactiveTabBg = new(0.08f, 0.08f, 0.09f, 1f);

        private void OnEnable()
        {
            _collectTablesGenericMethod = typeof(RuntimeDatabaseWindow).GetMethod(
                nameof(CollectTablesOfType), BindingFlags.Static | BindingFlags.NonPublic);
            _collectEntriesGenericMethod = typeof(RuntimeDatabaseWindow).GetMethod(
                nameof(CollectEntriesOfType), BindingFlags.Static | BindingFlags.NonPublic);

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

            BuildHeader(root);
            BuildStatsRow(root);

            _tabHeaderContainer = new VisualElement();
            _tabHeaderContainer.style.flexDirection = FlexDirection.Row;
            _tabHeaderContainer.style.backgroundColor = InactiveTabBg;
            _tabHeaderContainer.style.borderBottomWidth = 1;
            _tabHeaderContainer.style.borderBottomColor = AccentBorderColor;
            _tabHeaderContainer.style.marginTop = 6;
            root.Add(_tabHeaderContainer);

            CreateTabs();

            _tabContentContainer = new VisualElement();
            _tabContentContainer.style.flexGrow = 1;
            root.Add(_tabContentContainer);

            BuildTablesView();
            BuildSpawningView();
            SwitchTab(ActiveTab.Tables);
            BuildPlayModeOverlay();

            rootVisualElement.schedule.Execute(OnLiveUpdate).Every(150);
        }

        private void BuildHeader(VisualElement root)
        {
            var header = new VisualElement();
            header.style.backgroundColor = new Color(0.12f, 0.13f, 0.15f, 0.7f);
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = DarkBorder;
            header.style.marginTop = 8;
            header.style.marginLeft = 8;
            header.style.marginRight = 8;
            SetPadding(header, 12, 12, 15, 15);
            SetRadius(header, 8);
            root.Add(header);

            var titleContainer = new VisualElement();
            var title = new Label("Runtime Database Viewer");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.color = Color.white;
            titleContainer.Add(title);

            var subtitle = new Label("Unmanaged ECS runtime table & prefab streaming diagnostics");
            subtitle.style.fontSize = 10;
            subtitle.style.color = new Color(0.55f, 0.55f, 0.6f, 1f);
            subtitle.style.marginTop = 2;
            titleContainer.Add(subtitle);
            header.Add(titleContainer);

            var headerRight = new VisualElement();
            headerRight.style.flexDirection = FlexDirection.Row;
            headerRight.style.alignItems = Align.Center;

            _statusDot = new VisualElement();
            _statusDot.style.width = 8;
            _statusDot.style.height = 8;
            _statusDot.style.marginRight = 6;
            SetRadius(_statusDot, 4);
            headerRight.Add(_statusDot);

            _statusLabel = new Label("No World");
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.marginRight = 12;
            headerRight.Add(_statusLabel);

            _rescanBtn = new Button(RebuildHashRegistry)
            {
                text = $"Scan Assets ({_hashToStringRegistry.Count} Keys)"
            };
            _rescanBtn.style.backgroundColor = new Color(0.2f, 0.21f, 0.23f, 1f);
            _rescanBtn.style.color = Color.white;
            SetBorder(_rescanBtn, 0, Color.clear);
            SetRadius(_rescanBtn, 4);
            SetPadding(_rescanBtn, 4, 4, 10, 10);
            headerRight.Add(_rescanBtn);

            header.Add(headerRight);
        }

        private void BuildStatsRow(VisualElement root)
        {
            var statsRow = new VisualElement();
            statsRow.style.flexDirection = FlexDirection.Row;
            statsRow.style.justifyContent = Justify.SpaceBetween;
            statsRow.style.marginTop = 10;
            statsRow.style.marginLeft = 4;
            statsRow.style.marginRight = 4;

            _statTotalTablesVal = CreateStatCard(statsRow, "Total Tables", "0", AccentBlue);
            _statRegistryKeysVal = CreateStatCard(statsRow, "Registry Keys", "0", AccentPurple);
            _statCachedPrefabsVal = CreateStatCard(statsRow, "Cached Prefabs", "0", AccentGreen);
            _statActiveStreamsVal = CreateStatCard(statsRow, "Active Streams", "0", AccentGold);

            root.Add(statsRow);
        }

        private void OnLiveUpdate()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            bool isWorldActive = world != null && world.IsCreated;

            if (_playModeOverlay != null)
                _playModeOverlay.style.display = isWorldActive ? DisplayStyle.None : DisplayStyle.Flex;

            if (_statusDot != null && _statusLabel != null && _rescanBtn != null)
            {
                _statusDot.style.backgroundColor = isWorldActive ? AccentGreen : AccentRed;
                _statusLabel.text = isWorldActive
                    ? (EditorApplication.isPlaying ? "DefaultWorld (Play)" : "DefaultWorld (Editor)")
                    : "No Active ECS World";
                _statusLabel.style.color = isWorldActive ? AccentGreen : AccentRed;
                _rescanBtn.text = $"Scan Assets ({_hashToStringRegistry.Count} Keys)";
            }

            int currentTableCount = 0;
            int currentCacheCount = 0;
            int currentCallCount = 0;

            if (isWorldActive)
            {
                var em = world.EntityManager;
                var discovered = DiscoverAllTables(em);
                currentTableCount = discovered.Count;

                var systemHandle = world.GetExistingSystem<InstantiateEntitySystem>();
                if (systemHandle != SystemHandle.Null)
                {
                    ref var system = ref world.Unmanaged.GetUnsafeSystemRef<InstantiateEntitySystem>(systemHandle);
                    if (system.PrefabCache.IsCreated)
                        currentCacheCount = system.PrefabCache.Count;
                }

                using var qCalls = em.CreateEntityQuery(ComponentType.ReadOnly<InstantiateEntityPrefabCall>());
                currentCallCount = qCalls.CalculateEntityCount();
            }

            if (_statTotalTablesVal != null) _statTotalTablesVal.text = currentTableCount.ToString();
            if (_statRegistryKeysVal != null) _statRegistryKeysVal.text = _hashToStringRegistry.Count.ToString();
            if (_statCachedPrefabsVal != null) _statCachedPrefabsVal.text = currentCacheCount.ToString();
            if (_statActiveStreamsVal != null) _statActiveStreamsVal.text = currentCallCount.ToString();

            bool changed = isWorldActive != _lastWorldActiveState
                || _currentTab != _lastTab
                || _selectedTableKey != _lastSelectedTableKey
                || _selectedValueType != _lastSelectedValueType
                || _searchQuery != _lastSearchQuery
                || currentTableCount != _lastTableCount
                || currentCacheCount != _lastCacheCount
                || currentCallCount != _lastCallCount;

            _lastWorldActiveState = isWorldActive;
            _lastTab = _currentTab;
            _lastSelectedTableKey = _selectedTableKey;
            _lastSelectedValueType = _selectedValueType;
            _lastSearchQuery = _searchQuery;
            _lastTableCount = currentTableCount;
            _lastCacheCount = currentCacheCount;
            _lastCallCount = currentCallCount;

            if (!changed) return;

            if (_currentTab == ActiveTab.Tables)
            {
                RefreshTableList(_tableListScroll);
                RefreshTableDetails(_tableDetailsScroll);
            }
            else
            {
                RefreshPrefabCache(_prefabCacheScroll);
                RefreshPendingCalls(_pendingCallsScroll);
            }
        }

        private void CreateTabs()
        {
            _tabHeaderContainer.Clear();
            _tabHeaderContainer.Add(CreateTabButton("Database Tables", ActiveTab.Tables));
            _tabHeaderContainer.Add(CreateTabButton("Spawn & Prefab Cache", ActiveTab.Spawning));
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
            tabBtn.style.backgroundColor = isActive ? new Color(0.14f, 0.16f, 0.2f, 0.7f) : Color.clear;
            label.style.color = isActive ? Color.white : new Color(0.6f, 0.6f, 0.65f, 1f);
            tabBtn.style.borderBottomWidth = isActive ? 2 : 1;
            tabBtn.style.borderBottomColor = isActive ? AccentBlue : AccentBorderColor;

            tabBtn.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) SwitchTab(tab);
            });
            tabBtn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (_currentTab != tab)
                {
                    tabBtn.style.backgroundColor = new Color(0.13f, 0.14f, 0.15f, 0.5f);
                    label.style.color = Color.white;
                }
            });
            tabBtn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (_currentTab != tab)
                {
                    tabBtn.style.backgroundColor = Color.clear;
                    label.style.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                }
            });

            return tabBtn;
        }

        private void SwitchTab(ActiveTab tab)
        {
            _currentTab = tab;
            CreateTabs();

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

        private static Label CreateStatCard(VisualElement parent, string title, string initialValue, Color accentColor)
        {
            var card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.flexShrink = 1;
            card.style.backgroundColor = new Color(0.13f, 0.14f, 0.16f, 0.6f);
            card.style.marginLeft = 4;
            card.style.marginRight = 4;
            SetBorder(card, 1, AccentBorderColor);
            SetRadius(card, 6);
            SetPadding(card, 8, 8, 12, 12);

            var titleLabel = new Label(title.ToUpperInvariant());
            titleLabel.style.fontSize = 9;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.6f, 0.6f, 0.65f, 1f);
            card.Add(titleLabel);

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
            _playModeOverlay.style.top = 110;
            _playModeOverlay.style.bottom = 0;
            _playModeOverlay.style.left = 0;
            _playModeOverlay.style.right = 0;
            _playModeOverlay.style.backgroundColor = new Color(0.08f, 0.08f, 0.09f, 0.95f);
            _playModeOverlay.style.justifyContent = Justify.Center;
            _playModeOverlay.style.alignItems = Align.Center;

            var card = new VisualElement();
            card.style.width = 440;
            card.style.backgroundColor = new Color(0.14f, 0.15f, 0.17f, 0.85f);
            card.style.alignItems = Align.Center;
            SetBorder(card, 1, AccentBorderColor);
            SetRadius(card, 8);
            SetPadding(card, 20, 20, 25, 25);

            var title = new Label("NO ACTIVE ECS WORLD FOUND");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            title.style.color = AccentRed;
            title.style.marginBottom = 10;
            card.Add(title);

            var desc = new Label("Enter Play Mode to bake runtime database tables, start prefab streaming systems, and inspect unmanaged caches.");
            desc.style.fontSize = 11;
            desc.style.color = new Color(0.7f, 0.7f, 0.75f, 1f);
            desc.style.unityTextAlign = TextAnchor.MiddleCenter;
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginBottom = 15;
            card.Add(desc);

            var scanBtn = new Button(RebuildHashRegistry)
            {
                text = $"Scan Static Mappings ({_hashToStringRegistry.Count} Keys)"
            };
            scanBtn.style.backgroundColor = AccentBlue;
            scanBtn.style.color = Color.white;
            scanBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetBorder(scanBtn, 0, Color.clear);
            SetRadius(scanBtn, 4);
            SetPadding(scanBtn, 6, 6, 14, 14);
            card.Add(scanBtn);

            _playModeOverlay.Add(card);
            rootVisualElement.Add(_playModeOverlay);
        }

        #region TAB 1: TABLES VIEW

        private void BuildTablesView()
        {
            _tablesView = new VisualElement();
            _tablesView.style.flexGrow = 1;

            var splitView = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1;
            _tablesView.Add(splitView);

            var leftPane = new VisualElement();
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.style.backgroundColor = PanelBgColor;
            SetPadding(leftPane, 10, 0, 6, 6);
            splitView.Add(leftPane);

            _tableListScroll = new ScrollView(ScrollViewMode.Vertical);
            _tableListScroll.style.flexGrow = 1;
            leftPane.Add(_tableListScroll);

            var rightPane = new VisualElement();
            rightPane.style.flexDirection = FlexDirection.Column;
            SetPadding(rightPane, 10, 0, 10, 10);
            splitView.Add(rightPane);

            var searchHeader = new VisualElement();
            searchHeader.style.flexDirection = FlexDirection.Row;
            searchHeader.style.alignItems = Align.Center;
            searchHeader.style.marginBottom = 10;
            rightPane.Add(searchHeader);

            var searchLabel = new Label("Filter entries:");
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
                container.Add(CreateEmptyLabel("No active ECS World."));
                return;
            }

            var tables = DiscoverAllTables(world.EntityManager);
            if (tables.Count == 0)
            {
                container.Add(CreateEmptyLabel("No baked RuntimeTable<T> singletons found."));
                return;
            }

            Type currentGroupType = null;
            for (int i = 0; i < tables.Count; i++)
            {
                var info = tables[i];
                if (info.ValueType != currentGroupType)
                {
                    currentGroupType = info.ValueType;
                    container.Add(CreateGroupHeader(info.TypeDisplayName));
                }

                container.Add(CreateTableButton(info));
            }
        }

        private VisualElement CreateGroupHeader(string title)
        {
            var lbl = new Label(title);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.fontSize = 11;
            lbl.style.marginTop = 10;
            lbl.style.marginBottom = 4;
            lbl.style.paddingLeft = 4;
            lbl.style.color = new Color(0.65f, 0.75f, 0.85f, 1f);
            return lbl;
        }

        private Button CreateTableButton(DiscoveredTableInfo info)
        {
            var tableName = ResolveHash(info.TableKey);
            bool isSelected = _selectedTableKey == info.TableKey && _selectedValueType == info.ValueType;

            var btn = new Button(() =>
            {
                _selectedTableKey = info.TableKey;
                _selectedValueType = info.ValueType;
                RefreshTableDetails(_tableDetailsScroll);
                RefreshTableList(_tableListScroll);
            });

            var autoBakeTag = _autoBakeRegistry.Contains(info.TableKey) ? " [Catalog]" : "";
            btn.text = $"{tableName}{autoBakeTag}  [{info.EntryCount}]";
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            btn.style.marginBottom = 2;
            SetBorder(btn, 0, Color.clear);
            SetRadius(btn, 3);
            SetPadding(btn, 4, 4, 8, 8);

            btn.style.backgroundColor = isSelected ? ActiveSelectionColor : new Color(0.2f, 0.2f, 0.2f, 0.4f);
            btn.style.color = isSelected ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1f);

            btn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!isSelected) btn.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!isSelected) btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.4f);
            });

            return btn;
        }

        private void RefreshTableDetails(VisualElement container)
        {
            container.Clear();

            if (_selectedTableKey == 0 || _selectedValueType == null)
            {
                var welcome = CreateEmptyLabel("Select a loaded table on the left to inspect its entries.");
                welcome.style.marginTop = 40;
                welcome.style.unityTextAlign = TextAnchor.MiddleCenter;
                container.Add(welcome);
                return;
            }

            var tableName = ResolveHash(_selectedTableKey);
            var autoBakeNotice = _autoBakeRegistry.Contains(_selectedTableKey) ? "  [Prefab Catalog]" : "";
            var header = new Label($"Table: {tableName}{autoBakeNotice}  ({FormatTypeName(_selectedValueType)})");
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

            var rows = new List<EntryRowData>();
            _collectEntriesGenericMethod?.MakeGenericMethod(_selectedValueType)
                .Invoke(null, new object[] { world.EntityManager, _selectedTableKey, rows });

            int matchCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var entry = rows[i];
                var itemName = ResolveHash(entry.ObjectKey);

                if (!string.IsNullOrEmpty(_searchQuery) &&
                    itemName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.ObjectKey.ToString("X8").IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.ValueText.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var row = CreateTableRow(matchCount % 2 == 0);

                var nameLbl = new Label(itemName);
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.flexGrow = 1;
                row.Add(nameLbl);

                var hashLbl = new Label($"0x{entry.ObjectKey:X8}");
                hashLbl.style.color = Color.gray;
                hashLbl.style.fontSize = 11;
                hashLbl.style.marginRight = 16;
                row.Add(hashLbl);

                var valLbl = new Label(entry.ValueText);
                valLbl.style.color = entry.ValueColor;
                valLbl.style.fontSize = 11;
                row.Add(valLbl);

                container.Add(row);
                matchCount++;
            }

            if (matchCount == 0)
            {
                container.Add(CreateEmptyLabel("No entries found matching search query."));
            }
        }

        #endregion

        #region TAB 2: SPAWNING & PREFAB CACHE VIEW

        private void BuildSpawningView()
        {
            _spawningView = new VisualElement();
            _spawningView.style.flexGrow = 1;
            SetPadding(_spawningView, 10, 0, 12, 12);

            _prefabCacheScroll = CreatePanelSection(_spawningView, "Cached Prefab Entities (InstantiateEntitySystem)", 180, false);
            _pendingCallsScroll = CreatePanelSection(_spawningView, "In-Progress Spawning Calls (Active Prefab Streaming)", 0, true);
        }

        private static ScrollView CreatePanelSection(VisualElement parent, string headerText, float fixedHeight, bool flexGrow)
        {
            var header = new Label(headerText);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 13;
            header.style.color = Color.white;
            header.style.marginBottom = 5;
            parent.Add(header);

            var box = new VisualElement();
            if (flexGrow) box.style.flexGrow = 1;
            else box.style.height = fixedHeight;
            box.style.backgroundColor = PanelBgColor;
            box.style.marginBottom = 12;
            SetBorder(box, 1, AccentBorderColor);
            SetRadius(box, 4);
            parent.Add(box);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            SetPadding(scroll, 6, 6, 8, 8);
            box.Add(scroll);
            return scroll;
        }

        private void RefreshPrefabCache(VisualElement container)
        {
            container.Clear();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                container.Add(CreateEmptyLabel("No active World."));
                return;
            }

            var systemHandle = world.GetExistingSystem<InstantiateEntitySystem>();
            if (systemHandle == SystemHandle.Null)
            {
                container.Add(CreateEmptyLabel("InstantiateEntitySystem not active in this World."));
                return;
            }

            ref var system = ref world.Unmanaged.GetUnsafeSystemRef<InstantiateEntitySystem>(systemHandle);
            var cache = system.PrefabCache;

            if (!cache.IsCreated || cache.Count == 0)
            {
                container.Add(CreateEmptyLabel("Prefab cache is currently empty."));
                return;
            }

            int idx = 0;
            foreach (var kvp in cache)
            {
                RuntimeDB.UnpackCacheKey(kvp.Key, out uint tableKey, out uint itemKey);

                var row = CreateTableRow(idx % 2 == 0);
                var label = new Label($"<b>{ResolveHash(tableKey)}</b> / {ResolveHash(itemKey)}");
                label.style.flexGrow = 1;
                row.Add(label);

                var hexLabel = new Label($"0x{kvp.Key:X16}");
                hexLabel.style.color = new Color(0.5f, 0.5f, 0.55f, 1f);
                hexLabel.style.fontSize = 10;
                hexLabel.style.marginRight = 12;
                row.Add(hexLabel);

                row.Add(CreateEntityBadge(world, kvp.Value));
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
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<InstantiateEntityPrefabCall>());

            if (query.IsEmpty)
            {
                container.Add(CreateEmptyLabel("No active spawning / prefab loading streams."));
                return;
            }

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var ent = entities[i];
                var call = em.GetComponentData<InstantiateEntityPrefabCall>(ent);
                var row = CreateTableRow(i % 2 == 0);

                var label = new Label($"<b>{ResolveHash(call.TableKey)}</b> / {ResolveHash(call.Key)}  x{call.InstantiateCount}");
                label.style.flexGrow = 1;
                row.Add(label);

                row.Add(CreateEntityBadge(world, ent));
                if (call.Parent != Entity.Null)
                    row.Add(CreateEntityBadge(world, call.Parent, $"Parent {call.Parent.Index}:{call.Parent.Version}"));

                bool isLoading = em.HasComponent<RequestEntityPrefabLoaded>(ent);
                var statusLbl = new Label(isLoading ? "STREAMING" : "QUEUED");
                statusLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                statusLbl.style.fontSize = 10;
                statusLbl.style.color = isLoading ? AccentGold : AccentGreen;
                statusLbl.style.backgroundColor = isLoading
                    ? new Color(0.3f, 0.2f, 0.05f, 0.8f)
                    : new Color(0.05f, 0.2f, 0.05f, 0.8f);
                SetPadding(statusLbl, 2, 2, 6, 6);
                SetRadius(statusLbl, 3);
                row.Add(statusLbl);

                container.Add(row);
            }
        }

        #endregion

        #region DYNAMIC TABLE DISCOVERY

        private List<DiscoveredTableInfo> DiscoverAllTables(EntityManager em)
        {
            RefreshRegisteredTableTypes();

            var results = new List<DiscoveredTableInfo>();
            if (_collectTablesGenericMethod == null) return results;

            for (int i = 0; i < _cachedTableValueTypes.Count; i++)
            {
                var valueType = _cachedTableValueTypes[i];
                _collectTablesGenericMethod.MakeGenericMethod(valueType)
                    .Invoke(null, new object[] { em, results });
            }

            return results;
        }

        private void RefreshRegisteredTableTypes()
        {
            if (_isTableTypesDiscovered) return;

            _isTableTypesDiscovered = true;
            _cachedTableValueTypes.Clear();

            foreach (var typeInfo in TypeManager.AllTypes)
            {
                var type = typeInfo.Type;
                if (type == null || !type.IsGenericType) continue;
                if (type.GetGenericTypeDefinition() != typeof(RuntimeTable<>)) continue;

                _cachedTableValueTypes.Add(type.GetGenericArguments()[0]);
            }

            _cachedTableValueTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        private static void CollectTablesOfType<TValue>(EntityManager em, List<DiscoveredTableInfo> results)
            where TValue : unmanaged
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<TValue>>());
            if (!query.HasSingleton<RuntimeTable<TValue>>()) return;

            var tbl = query.GetSingleton<RuntimeTable<TValue>>();
            if (!tbl.IsCreated) return;

            ref var tables = ref tbl.Blob.Value.Tables;
            string typeName = FormatTypeName(typeof(TValue));

            for (int i = 0; i < tables.Length; i++)
            {
                results.Add(new DiscoveredTableInfo
                {
                    ValueType = typeof(TValue),
                    TypeDisplayName = typeName,
                    TableKey = tables[i].TableKey,
                    EntryCount = tables[i].Count
                });
            }
        }

        private static void CollectEntriesOfType<TValue>(EntityManager em, uint tableKey, List<EntryRowData> results)
            where TValue : unmanaged
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeTable<TValue>>());
            if (!query.HasSingleton<RuntimeTable<TValue>>()) return;

            var tbl = query.GetSingleton<RuntimeTable<TValue>>();
            if (!tbl.IsCreated) return;

            ref var root = ref tbl.Blob.Value;
            if (!TryFindTableMetadata(ref root.Tables, tableKey, out var meta)) return;

            for (int i = 0; i < meta.Count; i++)
            {
                ref var entry = ref root.Entries[meta.StartIndex + i];
                FormatValue(entry.Value, out var text, out var color);
                results.Add(new EntryRowData
                {
                    ObjectKey = entry.ObjectKey,
                    ValueText = text,
                    ValueColor = color
                });
            }
        }

        private static bool TryFindTableMetadata(ref BlobArray<TableMetadata> tables, uint key, out TableMetadata metadata)
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
                if (midKey < key) low = mid + 1;
                else high = mid - 1;
            }
            metadata = default;
            return false;
        }

        private static void FormatValue<TValue>(TValue value, out string text, out Color color)
            where TValue : unmanaged
        {
            if (value is EntityPrefabReference prefabRef)
            {
                text = $"AssetGUID: {prefabRef.AssetGUID}";
                color = new Color(0.4f, 0.8f, 0.4f, 1f);
                return;
            }

            text = value.ToString();
            color = new Color(0.55f, 0.78f, 0.95f, 1f);
        }

        private static string FormatTypeName(Type type)
        {
            if (!type.IsGenericType) return type.Name;

            var baseName = type.Name;
            int tick = baseName.IndexOf('`');
            if (tick > 0) baseName = baseName.Substring(0, tick);

            var args = type.GetGenericArguments();
            var argNames = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
                argNames[i] = FormatTypeName(args[i]);

            return $"{baseName}<{string.Join(", ", argNames)}>";
        }

        #endregion

        #region HASH REGISTRY & UI HELPERS

        private void RebuildHashRegistry()
        {
            _hashToStringRegistry.Clear();
            _autoBakeRegistry.Clear();
            _isRegistryInitialized = true;
            _isTableTypesDiscovered = false;

            var guids = AssetDatabase.FindAssets("t:RuntimeTableSoBase");
            if (guids == null) return;

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<RuntimeTableSoBase>(path);
                if (asset == null) continue;

                var tableName = asset.TableName;
                var tableHash = RuntimeDB.Hash32(tableName);
                _hashToStringRegistry[tableHash] = tableName;

                if (asset is IRuntimePrefabTable)
                    _autoBakeRegistry.Add(tableHash);

                var dataField = asset.GetType().GetField("data", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (dataField?.GetValue(asset) is not IEnumerable dataArray) continue;

                foreach (var item in dataArray)
                {
                    if (item == null) continue;
                    var keyField = item.GetType().GetField("key", BindingFlags.Public | BindingFlags.Instance);
                    if (keyField?.GetValue(item) is string keyStr && !string.IsNullOrEmpty(keyStr))
                    {
                        _hashToStringRegistry[RuntimeDB.Hash32(keyStr)] = keyStr;
                    }
                }
            }
        }

        private string ResolveHash(uint hash) =>
            _hashToStringRegistry.TryGetValue(hash, out var name) ? name : $"0x{hash:X8}";

        private static VisualElement CreateTableRow(bool alternateBg)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            SetPadding(row, 6, 6, 8, 8);
            if (alternateBg) row.style.backgroundColor = AltRowBgColor;
            return row;
        }

        private static Label CreateEmptyLabel(string message)
        {
            var lbl = new Label(message);
            lbl.style.color = Color.gray;
            lbl.style.fontSize = 11;
            lbl.style.marginTop = 10;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            return lbl;
        }

        private static VisualElement CreateEntityBadge(World world, Entity entity, string label = null)
        {
            var badge = new Label(label ?? $"{entity.Index}:{entity.Version}");
            badge.style.color = AccentBlue;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.fontSize = 10;
            badge.style.marginRight = 8;
            badge.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.08f);
            SetPadding(badge, 2, 2, 5, 5);
            SetRadius(badge, 3);
            SetBorder(badge, 1, new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f));

            badge.RegisterCallback<MouseEnterEvent>(_ =>
            {
                badge.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.22f);
                SetBorder(badge, 1, AccentBlue);
            });
            badge.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                badge.style.backgroundColor = new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.08f);
                SetBorder(badge, 1, new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.4f));
            });
            badge.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) PingEntity(world, entity);
            });

            return badge;
        }

        private static void PingEntity(World world, Entity entity)
        {
            try
            {
                var proxyType = Type.GetType("Unity.Entities.Editor.EntitySelectionProxy, Unity.Entities.Editor");
                var selectMethod = proxyType?.GetMethod("SelectEntity", BindingFlags.Static | BindingFlags.Public);
                if (selectMethod != null)
                {
                    selectMethod.Invoke(null, new object[] { world, entity });
                    return;
                }
                Debug.LogWarning("[Runtime Database Viewer] EntitySelectionProxy.SelectEntity not found.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Runtime Database Viewer] PingEntity failed: {ex.Message}");
            }
        }

        private static void SetPadding(VisualElement el, float top, float bottom, float left, float right)
        {
            el.style.paddingTop = top;
            el.style.paddingBottom = bottom;
            el.style.paddingLeft = left;
            el.style.paddingRight = right;
        }

        private static void SetRadius(VisualElement el, float r)
        {
            el.style.borderTopLeftRadius = r;
            el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r;
            el.style.borderBottomRightRadius = r;
        }

        private static void SetBorder(VisualElement el, float width, Color color)
        {
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
            el.style.borderTopColor = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor = color;
            el.style.borderRightColor = color;
        }

        #endregion
    }
}
#endif
