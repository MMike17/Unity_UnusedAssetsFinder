using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Tool used to find unused assets</summary>
public class UnusedAssetsFinder : EditorWindow
{
	private const string RESULTS_KEY = nameof(UnusedAssetsFinder) + "_Results";
	private const int MAX_FILE_SIZE = 100000;
	private const string BUILD_SETTINGS_FLAG = "BuildSettings";
	private const string ASSET_BUNDLE_FLAG = "(Asset bundle) ";
	private const string GUID_FLAG = "guid:";
	private const int RESULTS_PER_PAGE = 30;
	private const int FILTER_WIDTH = 200;
	private const int PREVIEW_WIDTH = 60;
	private const float GREY_VALUE = 0.8f;
	private const int TARGET_FPS = 30;
	private const string RECOVERY_DIR = ".recovery";

	private static readonly string[] UNITY_EXTENSIONS = new string[] {
		// 3D model
		".fbx", // 0
		".mb",
		".ma",
		".max",
		".jas",
		".dae",
		".dxf",
		".obj",
		".c4d",
		".blend",
		".lxo",
		".mesh",
		".3ds",
		".skp", // 13
		// Animation
		".anim", // 14
		".controller",
		".overrideController",
		".blendtree",
		".animset",
		".playable",
		".state",
		".statemachine",
		".signal",
		".transition", // 23
		// Assembly
		".asmdef", // 24
		".asmref", // 25
		// Audio
		".ogg", // 26
		".aif",
		".aiff",
		".flac",
		".wav",
		".mp3",
		".mod",
		".it",
		".s3m",
		".xm",
		".mixer", // 36
		// Avatar
		".ht", // 37
		".mask", // 38
		// Build report
		".buildreport", // 39
		// Curves
		".curves", // 40
		".curvesNormalized", // 41
		// Font
		".fontsettings", // 42
		".ttf",
		".dfont",
		".otf",
		".ttc", // 46
		// Independent hardware vendor
		".astc", // 47
		".dds",
		".ktx",
		".pvr", // 50
		// Localization
		".po", // 51
		// Material
		".mat" , // 52
		".cubemap" ,
		".physicMaterial" ,
		".physicsMaterial2D" , // 55
		// Plugin
		".dll", // 56
		".winmd",
		".so",
		".jar",
		".java",
		".kt",
		".aar",
		".suprx",
		".prx",
		".rpl",
		".cpp",
		".cc",
		".c",
		".h",
		".jslib",
		".jspre",
		".bc",
		".a",
		".m",
		".mm",
		".swift",
		".xib",
		".bundle",
		".dylib",
		".config", // 80
		// Prefab
		".prefab", // 81
		// Preset
		".preset", // 82
		// Scene
		".rsp", // 83
		".unity", // 84
		// Script
		".cs", // 85
		// Scriptable object
		".asset", // 86
		// Shader
		".compute", // 87
		".raytrace",
		".cginc",
		".cg",
		".glslinc",
		".hlsl",
		".shader",
		".shadervariants",
		".shadergraph",
		".shadersubgraph", // 96
		// Substance
		".sbsar", // 97
		// Colors
		".colors", // 98
		".gradients", // 99
		// Terrain
		".brush", // 100
		".terrainlayer",
		".spm",
		".st", // 103
		// Texture
		".jpg", // 104
		".jpeg",
		".tif",
		".tiff",
		".tga",
		".gif",
		".png",
		".psd",
		".bmp",
		".iff",
		".pict",
		".pic",
		".pct",
		".exr",
		".hdr",
		".renderTexture",
		".texture2D",
		".spriteatlas",
		".webCamTexture", // 122
		// GUI skin
		".guiskin", // 123
		// Video
		".avi", // 124
		".asf",
		".wmv",
		".mov",
		".dv",
		".mp4",
		".m4v",
		".mpg",
		".mpeg",
		".ogv",
		".vp8",
		".webm", // 135
		// Visual effects
		".flare", // 136
		".giparams",
		".vfx",
		".vfxoperator",
		".vfxblock",
		".particleCurves",
		".particleCurvesSigned",
		".particleDoubleCurves",
		".particleDoubleCurvesSigned",
		".lighting" // 145
	};

	private static readonly int[] YAML_INDEXES = new int[] { 2, 5, 7, 9, 12, 14, 15, 18, 21, 22, 24 };

	private static UnusedAssetsFinder _instance;
	private static UnusedAssetsFinder Instance
	{
		get
		{
			if (_instance == null)
				RefreshWindow();

			return _instance;
		}
	}

	private static string recoveryDir => Path.Combine(Application.dataPath, RECOVERY_DIR);

	private enum AssetType
	{
		None,
		_3D_model,
		Animation,
		Assembly,
		Audio,
		Avatar,
		Build_report,
		Curves,
		Editor_tool,
		Font,
		Independent_hardware_vendor,
		Localization,
		Material,
		Plugin,
		Prefab,
		Preset,
		Scene,
		Script,
		Scriptable_object,
		Shader,
		Substance,
		Colors,
		Terrain,
		Texture,
		GUI_skin,
		Video,
		Visual_effect,
		Text
	}

	private enum AnalysisStatus
	{
		Before,
		Running_Sync,
		Async_Asset_Indexing,
		Async_Analysis,
		Async_Ref_Analysis,
		Results
	}

	private float pathWidth => Instance.minSize.x - 20;
	private float labelWidth => pathWidth - FILTER_WIDTH - PREVIEW_WIDTH - 30;
	private Color fadedColor => Color.white * GREY_VALUE;
	private int maxPageIndex => Mathf.FloorToInt((float)selectedSortResults.Count / RESULTS_PER_PAGE);

	// styles
	private GUIStyle titleStyle;
	private GUIStyle rightStyle;
	private GUIStyle textStyle;
	private GUIStyle buttonStyle;
	private GUIStyle centerStyle;

	// shared
	private AnalysisResults results;
	private Dictionary<string, string> resPathToGuid;
	private Dictionary<string, List<string>> classToGuids;
	private Dictionary<string, string> classToEditor;
	private Dictionary<string, List<string>> guidToReferences;
	private List<KeyValuePair<string, string>> selectedRefResults;
	private List<KeyValuePair<string, string>> selectedSearchResults;
	private List<KeyValuePair<string, string>> selectedSortResults;
	private List<KeyValuePair<string, string>> selectedPageResults;
	private Dictionary<string, bool> guidToFoldoutStatus;
	private Dictionary<string, bool> guidToSelectedStatus;
	private Vector2 scroll;
	private AnalysisStatus status;
	private int progressIndex;
	private bool forceSearch;
	private bool forceSort;


	// async part
	private bool cancelFlag;
	private string[] asyncAssetPaths;
	private List<KeyValuePair<string, List<string>>> asyncAssetBundles;
	private int asyncMaxIndex;
	private List<string> asyncAssetToValidate;

	private bool _showReferencedAssets;
	private bool ShowReferencedAssets
	{
		get => _showReferencedAssets;
		set
		{
			_showReferencedAssets = value;
			selectedRefResults = new List<KeyValuePair<string, string>>();

			foreach (KeyValuePair<string, string> pair in results.guidToPath)
			{
				if (results.guidToRefStatus[pair.Key] == value)
					selectedRefResults.Add(pair);
			}

			forceSearch = true;
			Search = Search;
		}
	}

	private string _search;
	private string Search
	{
		get => _search;
		set
		{
			if (!forceSearch && _search == value)
				return;

			selectedSearchResults = new List<KeyValuePair<string, string>>();

			foreach (KeyValuePair<string, string> pair in selectedRefResults)
			{
				if (string.IsNullOrEmpty(value))
				{
					selectedSearchResults.Add(pair);
					continue;
				}

				FileInfo info = new FileInfo(pair.Value);

				if (info.Name.ToLower().Trim().Contains(value.ToLower().Trim()))
					selectedSearchResults.Add(pair);
			}

			_search = value;
			forceSearch = false;

			forceSort = true;
			SortType = SortType;
		}
	}

	private int _sortType;
	private int SortType
	{
		get => _sortType;
		set
		{
			if (!forceSort && _sortType == value)
				return;

			selectedSortResults = new List<KeyValuePair<string, string>>();

			foreach (KeyValuePair<string, string> pair in selectedSearchResults)
			{
				FileInfo info = new FileInfo(pair.Value);

				if (info.Exists && (value == (int)AssetType.None || (int)GetAssetType(info) == value))
					selectedSortResults.Add(pair);
			}

			_sortType = value;
			forceSort = false;
			PageIndex = 0;
		}
	}

	private int _pageIndex;
	private int PageIndex
	{
		get => _pageIndex;
		set
		{
			_pageIndex = value;

			selectedPageResults = new List<KeyValuePair<string, string>>();
			guidToFoldoutStatus = new Dictionary<string, bool>();
			int maxIndex = Mathf.Min((value + 1) * RESULTS_PER_PAGE, selectedSortResults.Count);

			for (int i = value * RESULTS_PER_PAGE; i < maxIndex; i++)
			{
				selectedPageResults.Add(selectedSortResults[i]);
				guidToFoldoutStatus.Add(selectedPageResults[^1].Key, false);
			}

			scroll = Vector2.zero;
		}
	}

	private string[] _assetTypes;
	private string[] AssetTypes
	{
		get
		{
			if (_assetTypes == null)
			{
				List<string> names = new List<string>();

				foreach (AssetType type in Enum.GetValues(typeof(AssetType)))
					names.Add(CleanAssetType(type));

				_assetTypes = names.ToArray();
			}

			return _assetTypes;
		}
	}

	// Main tool flow
	[MenuItem("Tools/Unused Assets Finder")]
	public static void ShowEditorWindow()
	{
		RefreshWindow();

		// create recovery folder
		if (!Directory.Exists(recoveryDir))
			Directory.CreateDirectory(recoveryDir);

		// add to .gitignore
		string gitIgnorePath = Application.dataPath.Replace("Assets", ".gitignore");

		if (File.Exists(gitIgnorePath))
		{
			List<string> lines = new List<string>(File.ReadAllLines(gitIgnorePath));
			lines.Add("# Unused Assets Finder recovery folder");
			lines.Add("/[Aa]ssets/.recovery");
			lines.Add("");

			File.WriteAllLines(gitIgnorePath, lines);

			EditorUtility.DisplayDialog(
				".gitignore updated",
				"Your .gitignore file has been updated to not detect the recovery folder",
				"Okay"
			);
		}

		AssemblyReloadEvents.beforeAssemblyReload += CancelOperations;

		if (PlayerPrefs.HasKey(RESULTS_KEY))
			AskForLoading();
		else
			AskForProcess();
	}

	private static void AskForLoading()
	{
		string message = "Do you want to load the previous project analysis or start a new one ?";

		if (EditorUtility.DisplayDialog("Loading", message, "Load last results", "Analyze project"))
			LoadResults();
		else
			AskForProcess();
	}

	private static void LoadResults()
	{
		Instance.cancelFlag = false;

		if (!PlayerPrefs.HasKey(RESULTS_KEY))
			return;

		Instance.results = JsonUtility.FromJson<AnalysisResults>(PlayerPrefs.GetString(RESULTS_KEY));
		Instance.results.PostSerialization();

		Instance.status = AnalysisStatus.Results;
		Instance.ShowReferencedAssets = true;
		GenerateSelectionDictionary();
	}

	private static void AskForProcess()
	{
		string message = "This tool can run in synchronous mode (editor will be blocked during the process, but it will take less time) or asynchronous mode (editor won't be locked but it will take longer).";

		if (EditorUtility.DisplayDialog("Asset analysis", message, "Start sync", "Start async"))
		{
			SyncAssetsIndexing();
			SyncAnalysis();
			SyncRefAnalysis();
		}
		else
		{
			Instance.Show();
			StartAsyncIndexing();
		}
	}

	private static void StartAsyncIndexing()
	{
		Instance.cancelFlag = false;

		Instance.results.guidToPath = new Dictionary<string, string>();
		Instance.resPathToGuid = new Dictionary<string, string>();
		Instance.classToGuids = new Dictionary<string, List<string>>();
		Instance.classToEditor = new Dictionary<string, string>();
		Instance.progressIndex = 0;
		Instance.status = AnalysisStatus.Async_Asset_Indexing;

		EditorApplication.update += AsyncAssetIndexing;
	}

	private static void SyncAssetsIndexing()
	{
		Instance.status = AnalysisStatus.Running_Sync;

		Instance.results.guidToPath = new Dictionary<string, string>();
		Instance.resPathToGuid = new Dictionary<string, string>();
		Instance.classToGuids = new Dictionary<string, List<string>>();
		Instance.classToEditor = new Dictionary<string, string>();

		string[] paths = AssetDatabase.GetAllAssetPaths();
		FileInfo file;
		Instance.progressIndex = 0;

		foreach (string path in paths)
		{
			file = new FileInfo(path);

			if (IsProjectAsset(file))
			{
				string guid = GetAssetGUID(path);
				Instance.results.guidToPath.Add(guid, path);

				if (path.Contains("Resources"))
					Instance.resPathToGuid.Add(path, guid);

				if (GetAssetType(file) == AssetType.Script || GetAssetType(file) == AssetType.Editor_tool)
					ExtractClassNames(guid, path);
			}

			EditorUtility.DisplayProgressBar(
				"Step 1 : Indexing assets",
				"Indexing project assets and GUIDs (" + Instance.progressIndex + "/" + paths.Length + ")",
				(float)Instance.progressIndex / paths.Length
			);

			Instance.progressIndex++;
		}

		EditorUtility.ClearProgressBar();
	}

	private static void AsyncAssetIndexing()
	{
		if (CheckInterruption())
			return;

		long ticks = DateTime.Now.Ticks;
		long maxTicks = ticks + (10000000 / TARGET_FPS);

		if (Instance.asyncAssetPaths == null)
			Instance.asyncAssetPaths = AssetDatabase.GetAllAssetPaths();

		FileInfo file;
		string path;

		while (Instance.progressIndex < Instance.asyncAssetPaths.Length)
		{
			if (CheckInterruption())
				return;

			path = Instance.asyncAssetPaths[Instance.progressIndex];
			file = new FileInfo(path);

			if (IsProjectAsset(file))
			{
				string guid = GetAssetGUID(path);
				Instance.results.guidToPath.Add(guid, path);

				if (path.Contains("Resources"))
					Instance.resPathToGuid.Add(path, guid);

				if (GetAssetType(file) == AssetType.Script || GetAssetType(file) == AssetType.Editor_tool)
					ExtractClassNames(guid, path);
			}

			Instance.progressIndex++;

			// interruption
			if (DateTime.Now.Ticks >= maxTicks)
				return;
		}

		EditorApplication.update -= AsyncAssetIndexing;

		Instance.status = AnalysisStatus.Async_Analysis;
		Instance.guidToReferences = new Dictionary<string, List<string>>();
		Instance.results.guidToSources = new Dictionary<string, List<string>>();
		Instance.progressIndex = 0;

		EditorApplication.update += AsyncAnalysis;
	}

	private static void SyncAnalysis()
	{
		Instance.guidToReferences = new Dictionary<string, List<string>>();
		Instance.results.guidToSources = new Dictionary<string, List<string>>();
		int maxIndex = Instance.results.guidToPath.Count;
		Instance.progressIndex = 0;

		foreach (KeyValuePair<string, string> asset in Instance.results.guidToPath)
		{
			ManageFile(asset.Value, asset.Key);

			EditorUtility.DisplayProgressBar(
				"Step 2 : Analyzing references",
				"Analyzing project asset references (" + Instance.progressIndex + "/" + maxIndex + ")",
				(float)Instance.progressIndex / maxIndex
			);

			Instance.progressIndex++;
		}

		EditorUtility.ClearProgressBar();
	}

	private static void AsyncAnalysis()
	{
		if (CheckInterruption())
			return;

		long ticks = DateTime.Now.Ticks;
		long maxTicks = ticks + (10000000 / TARGET_FPS);

		while (Instance.progressIndex < Instance.results.guidToPath.Count)
		{
			if (CheckInterruption())
				return;

			KeyValuePair<string, string> asset = Instance.results.guidToPath.ElementAt(Instance.progressIndex);
			ManageFile(asset.Value, asset.Key);

			Instance.progressIndex++;

			// interruption
			if (DateTime.Now.Ticks >= maxTicks)
				return;
		}

		EditorApplication.update -= AsyncAnalysis;

		Instance.status = AnalysisStatus.Async_Ref_Analysis;
		Instance.results.guidToRefStatus = new Dictionary<string, bool>();
		Instance.progressIndex = 0;

		foreach (KeyValuePair<string, string> pair in Instance.results.guidToPath)
			Instance.results.guidToRefStatus.Add(pair.Key, false);

		EditorApplication.update += AsyncRefAnalysis;
	}

	private static void SyncRefAnalysis()
	{
		Instance.results.guidToRefStatus = new Dictionary<string, bool>();

		foreach (KeyValuePair<string, string> pair in Instance.results.guidToPath)
			Instance.results.guidToRefStatus.Add(pair.Key, false);

		List<KeyValuePair<string, List<string>>> assetBundles = new List<KeyValuePair<string, List<string>>>(
			Instance.guidToReferences.Where(item => item.Key.Contains(ASSET_BUNDLE_FLAG))
		);
		int maxIndex = SceneManager.sceneCount + assetBundles.Count;
		Instance.progressIndex = 0;
		List<string> assetsToValidate = new List<string>();
		int index = 0;

		// start with scenes
		while (index < SceneManager.sceneCount)
		{
			string sceneGuid = GetAssetGUID(SceneManager.GetSceneAt(index).path);
			assetsToValidate.Add(sceneGuid);

			EditorUtility.DisplayProgressBar(
				"Step 3 : Analyzing reference chains",
				"Analyzing chains of references (" + Instance.progressIndex + "/" + maxIndex + ")",
				(float)Instance.progressIndex / maxIndex
			);

			Instance.progressIndex++;
			index++;
		}

		// add asset bundles
		index = 0;
		while (index < assetBundles.Count)
		{
			assetBundles[index].Value.ForEach(guid => assetsToValidate.Add(guid));

			EditorUtility.DisplayProgressBar(
				"Step 3 : Analyzing reference chains",
				"Analyzing chains of references (" + Instance.progressIndex + "/" + maxIndex + ")",
				(float)Instance.progressIndex / maxIndex
			);

			Instance.progressIndex++;
			index++;
		}

		// start process
		index = 0;
		while (index < assetsToValidate.Count)
		{
			RegisterAssetRef(assetsToValidate, assetsToValidate[index]);

			EditorUtility.DisplayProgressBar(
				"Step 3 : Analyzing reference chains",
				"Analyzing chains of references (" + Instance.progressIndex + "/" + (maxIndex + assetsToValidate.Count) + ")",
				(float)Instance.progressIndex / (maxIndex + assetsToValidate.Count)
			);

			Instance.progressIndex++;
			index++;
		}

		EditorUtility.ClearProgressBar();
		Instance.status = AnalysisStatus.Results;
		Instance.ShowReferencedAssets = true;
		GenerateSelectionDictionary();

		Instance.results.PreSerialization();
		PlayerPrefs.SetString(RESULTS_KEY, JsonUtility.ToJson(Instance.results));
	}

	private static void AsyncRefAnalysis()
	{
		if (CheckInterruption())
			return;

		long ticks = DateTime.Now.Ticks;
		long maxTicks = ticks + (10000000 / TARGET_FPS);

		if (Instance.progressIndex == 0)
		{
			Instance.asyncAssetBundles = new List<KeyValuePair<string, List<string>>>(
				Instance.guidToReferences.Where(item => item.Key.Contains(ASSET_BUNDLE_FLAG))
			);
			Instance.asyncMaxIndex = SceneManager.sceneCount + Instance.asyncAssetBundles.Count;

			Instance.asyncAssetToValidate = new List<string>();
			string sceneGuid;
			int index = 0;

			// start with scene
			while (index < SceneManager.sceneCount)
			{
				sceneGuid = GetAssetGUID(SceneManager.GetSceneAt(Instance.progressIndex).path);
				Instance.asyncAssetToValidate.Add(sceneGuid);

				Instance.progressIndex++;
				index++;
			}

			// add asset bundle
			index = 0;
			while (index < Instance.asyncAssetBundles.Count)
			{
				Instance.asyncAssetBundles[index].Value.ForEach(guid => Instance.asyncAssetToValidate.Add(guid));

				Instance.progressIndex++;
				index++;
			}
		}

		if (CheckInterruption())
			return;

		string guid;

		while (Instance.progressIndex - Instance.asyncMaxIndex < Instance.asyncAssetToValidate.Count)
		{
			if (CheckInterruption())
				return;

			guid = Instance.asyncAssetToValidate[Instance.progressIndex - Instance.asyncMaxIndex];
			RegisterAssetRef(Instance.asyncAssetToValidate, guid);

			Instance.progressIndex++;

			// interruption
			if (DateTime.Now.Ticks >= maxTicks)
				return;
		}

		EditorApplication.update -= AsyncRefAnalysis;

		Instance.status = AnalysisStatus.Results;
		Instance.ShowReferencedAssets = true;
		GenerateSelectionDictionary();

		Instance.results.PreSerialization();
		PlayerPrefs.SetString(RESULTS_KEY, JsonUtility.ToJson(Instance.results));

		EditorUtility.DisplayDialog(
			"Async analysis finished.",
			"The async analysis of this project is finished.",
			"Close"
		);
	}

	private void OnGUI()
	{
		GenerateIfNeeded();

		EditorGUILayout.LabelField("Unused assers finder", titleStyle);
		EditorGUILayout.Space();

		switch (status)
		{
			case AnalysisStatus.Before:
				CenterDisplay(() =>
				{
					EditorGUILayout.HelpBox(
						"The operations have been interrupted, likely because of an assembly reload.",
						MessageType.Warning
					);
				});

				GUIDivider();

				CenterDisplay(() =>
				{
					if (PlayerPrefs.HasKey(RESULTS_KEY) && GUILayout.Button("Load last analysis"))
						LoadResults();

					EditorGUILayout.Space();

					if (GUILayout.Button("Sync analysis"))
					{
						SyncAssetsIndexing();
						SyncAnalysis();
						SyncRefAnalysis();
					}

					EditorGUILayout.Space();

					if (GUILayout.Button("Start async"))
						StartAsyncIndexing();
				});
				break;

			case AnalysisStatus.Async_Asset_Indexing:
				AsyncLoadingBar(
					"Step 1 : Indexing assets",
					(float)progressIndex / asyncAssetPaths.Length
				);
				break;

			case AnalysisStatus.Async_Analysis:
				AsyncLoadingBar(
					"Step 2 : Analyzing references",
					(float)progressIndex / results.guidToPath.Count
				);
				break;

			case AnalysisStatus.Async_Ref_Analysis:
				AsyncLoadingBar(
					"Step 3 : Analyzing reference chains",
					(float)progressIndex / asyncMaxIndex
				);
				break;

			case AnalysisStatus.Results:
				CenterDisplay(() =>
				{
					if (PlayerPrefs.HasKey(RESULTS_KEY) && GUILayout.Button("Load last analysis"))
						LoadResults();

					EditorGUILayout.Space();

					if (GUILayout.Button("Reload sync"))
					{
						SyncAssetsIndexing();
						SyncAnalysis();
						SyncRefAnalysis();
					}

					EditorGUILayout.Space();

					if (GUILayout.Button("Reload async"))
						StartAsyncIndexing();
				});

				GUIDivider();

				DisplayResults();
				break;
		}

		if (cancelFlag)
			AssemblyReloadEvents.beforeAssemblyReload -= CancelOperations;
	}

	private void DisplayResults()
	{
		// Mode selection
		CenterDisplay(() =>
		{
			Color color = ShowReferencedAssets ? Color.cyan : Color.white;

			DisplayColored(() =>
			{
				if (GUILayout.Button("Referenced assets") && !ShowReferencedAssets)
					ShowReferencedAssets = true;
			}, color);

			EditorGUILayout.Space();
			color = ShowReferencedAssets ? Color.white : Color.cyan;

			DisplayColored(() =>
			{
				if (GUILayout.Button("Unused assets") && ShowReferencedAssets)
					ShowReferencedAssets = false;
			}, color);

			EditorGUILayout.Space();
			SortType = EditorGUILayout.Popup(SortType, AssetTypes, GUILayout.Width(FILTER_WIDTH));
		});

		GUIDivider();

		// Assets operations
		CenterDisplay(() =>
		{
			EditorGUILayout.LabelField("Search : ", rightStyle, GUILayout.Width(65));
			Search = EditorGUILayout.TextField(Search);

			bool hasSelection = guidToSelectedStatus.Count(item => item.Value) > 0;
			bool hasRestore = Directory.GetFiles(recoveryDir).Length + Directory.GetDirectories(recoveryDir).Length > 0;

			if (hasSelection)
			{
				EditorGUILayout.Space();

				DisplayColored(
					() =>
					{
						if (GUILayout.Button("Remove assets"))
						{
							string message = "Are you sure you want to remove the selected " + guidToSelectedStatus.Count(item => item.Value) + " assets ?\n\nThe selected assests will be moved to a recovery folder instead of being deleted. They can be recovered with the \"Restore assets\" button.\n\nAssets in the recovery folder with the same name will be deleted.";

							if (EditorUtility.DisplayDialog("Asset removal", message, "Yes", "No"))
								MoveAssetsToRecovery();
						}
					},
					Color.red
				);
			}

			if (hasRestore)
			{
				EditorGUILayout.Space();

				DisplayColored(
					() =>
					{
						if (hasRestore && GUILayout.Button("Restore assets"))
						{
							string message = "Are you sure you want to recover all removed assets ?\nThis action will delete assets with the same name as recovered assets in the asset folder.";

							if (EditorUtility.DisplayDialog("Asset recovery", message, "Yes", "No"))
								RecoverAssets();
						}
					},
					Color.yellow
				);
			}
		});

		EditorGUILayout.Space();

		// Asset selection and titles
		EditorGUILayout.BeginHorizontal(new GUIStyle(GUI.skin.box));
		{
			int selectionState = 0;
			bool hasUnselected = false;

			foreach (KeyValuePair<string, string> pair in selectedSortResults)
			{
				// need this to fix race condition
				if (!guidToSelectedStatus.ContainsKey(pair.Key))
					continue;

				if (guidToSelectedStatus[pair.Key])
					selectionState = 2;
				else
					hasUnselected = true;
			}

			if (selectionState == 2 && hasUnselected)
				selectionState = 1;

			EditorGUI.showMixedValue = selectionState == 1;
			bool newState = EditorGUILayout.Toggle(selectionState == 2);
			EditorGUI.showMixedValue = false;

			if (selectedSortResults.Count != 0)
			{
				if (newState)
				{
					if (selectionState == 0 || selectionState == 1)
					{
						foreach (KeyValuePair<string, string> pair in selectedSortResults)
							guidToSelectedStatus[pair.Key] = true;
					}
				}
				else if (selectionState == 2)
				{
					foreach (KeyValuePair<string, string> pair in selectedSortResults)
						guidToSelectedStatus[pair.Key] = false;
				}
			}

			EditorGUILayout.LabelField("Asset name", textStyle, GUILayout.Width(labelWidth));
			EditorGUILayout.LabelField("(Asset type)  ", rightStyle, GUILayout.Width(FILTER_WIDTH));
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();

		// Display assets
		FileInfo info;

		scroll.x = 0;
		scroll = EditorGUILayout.BeginScrollView(scroll);
		{
			foreach (KeyValuePair<string, string> asset in selectedPageResults)
			{
				if (!asset.Equals(selectedPageResults.ElementAt(0)))
					DisplayColored(() => GUIDivider(), fadedColor);

				info = new FileInfo(asset.Value);

				// we need this to fix race issue
				if (!guidToSelectedStatus.ContainsKey(asset.Key))
					continue;

				EditorGUILayout.BeginHorizontal();
				{
					EditorGUILayout.Space();

					guidToSelectedStatus[asset.Key] = EditorGUILayout.Toggle(guidToSelectedStatus[asset.Key]);

					if (GUILayout.Button("Preview", GUILayout.Width(60)))
						PreviewPopup.Preview(asset.Value, GetAssetType(info));

					EditorGUILayout.LabelField(
						info.Name.Replace(info.Extension, ""),
						textStyle,
						GUILayout.Width(labelWidth)
					);

					DisplayColored(
						() => EditorGUILayout.LabelField(
							"(" + CleanAssetType(GetAssetType(info)) + ")",
							rightStyle,
							GUILayout.Width(FILTER_WIDTH)
						),
						fadedColor
					);
				}
				EditorGUILayout.EndHorizontal();

				DisplayColored(() =>
					EditorGUILayout.LabelField(asset.Value, textStyle, GUILayout.Width(pathWidth)),
					fadedColor
				);

				if (!results.guidToSources.ContainsKey(asset.Key))
					continue;

				DisplayColored(
					() =>
					{
						guidToFoldoutStatus[asset.Key] = EditorGUILayout.BeginFoldoutHeaderGroup(
							guidToFoldoutStatus[asset.Key],
							"Referenced in"
						);
						{
							if (guidToFoldoutStatus[asset.Key])
							{
								DisplayColored(() =>
								{
									foreach (string guid in results.guidToSources[asset.Key])
									{
										if (results.guidToPath.ContainsKey(guid))
											EditorGUILayout.LabelField("-\u00A0" + results.guidToPath[guid], textStyle);
										else
											EditorGUILayout.LabelField("-\u00A0" + guid, textStyle);
									}
								}, Color.white);
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
					},
					fadedColor
				);
			}
		}
		EditorGUILayout.EndScrollView();

		GUIDivider();
		EditorGUILayout.Space();

		// Page selection
		CenterDisplay(() =>
		{
			if (PageIndex > 0 && GUILayout.Button("<", GUILayout.Width(50)))
				PageIndex--;

			EditorGUILayout.LabelField(
				"page " + (PageIndex + 1) + "/" + (maxPageIndex + 1),
				centerStyle,
				GUILayout.Width(100)
			);

			if (PageIndex < maxPageIndex && GUILayout.Button(">", GUILayout.Width(50)))
				PageIndex++;
		});

		EditorGUILayout.Space();
		EditorGUILayout.Space();
	}

	private void MoveAssetsToRecovery()
	{
		// select assets
		List<(string, string)> selectedAssets = new List<(string, string)>();

		foreach (KeyValuePair<string, bool> pair in guidToSelectedStatus)
		{
			if (pair.Value)
				selectedAssets.Add((pair.Key, results.guidToPath[pair.Key]));
		}

		// delete assets
		progressIndex = 0;
		foreach ((string guid, string path) asset in selectedAssets)
		{
			DereferenceAsset(asset.guid);
			MoveAsset(
				Path.GetFullPath(asset.path),
				Path.Combine(recoveryDir, asset.path.Replace("\\", "/").Replace("Assets/", ""))
			);
			MoveAsset(
				Path.GetFullPath(asset.path) + ".meta",
				Path.Combine(recoveryDir, asset.path.Replace("\\", "/").Replace("Assets/", "")) + ".meta"
			);

			EditorUtility.DisplayProgressBar(
				"Deleting assets",
				"Deleting assets (" + progressIndex + "/" + selectedAssets.Count + ").",
				(float)progressIndex / selectedAssets.Count
			);

			progressIndex++;
		}

		EditorUtility.ClearProgressBar();

		string message = "All selected assets have been removed.";
		EditorUtility.DisplayDialog("Asset deletion done", message, "Ok");

		AssetDatabase.Refresh();
	}

	private void RecoverAssets()
	{
		List<string> recoveryAssets = GetFilesRecursive(recoveryDir);

		progressIndex = 0;
		foreach (string path in recoveryAssets)
		{
			string newPath = path.Replace("\\", "/").Replace(RECOVERY_DIR + "/", "");
			MoveAsset(path, newPath);

			EditorUtility.DisplayProgressBar(
				"Recovering assets",
				"Recovering assets (" + progressIndex + "/" + recoveryAssets.Count + ").",
				(float)progressIndex / recoveryAssets.Count
			);

			progressIndex++;
		}

		// clear .recovery folders
		foreach (string recoveryDirPath in Directory.GetDirectories(recoveryDir))
			Directory.Delete(recoveryDirPath, true);

		EditorUtility.ClearProgressBar();

		string message = "Assets have been recovered, new analysis required.";
		EditorUtility.DisplayDialog("Asset recovery done", message, "Ok");

		AssetDatabase.Refresh();
		status = AnalysisStatus.Before;
	}

	// Utility methods
	private static void RefreshWindow()
	{
		_instance = GetWindow<UnusedAssetsFinder>();
		_instance.titleContent = new GUIContent("Unused Assets Finder");
		_instance.minSize = new Vector2(600, 700);
		_instance.results = new AnalysisResults();
	}

	private static bool IsProjectAsset(FileInfo info)
	{
		return info.FullName.Replace("\\", "/").Contains(Application.dataPath) && info.Extension != string.Empty;
	}

	private static string GetAssetGUID(string path)
	{
		return AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
	}

	private static AssetType GetAssetType(FileInfo info)
	{
		int index = new List<string>(UNITY_EXTENSIONS).IndexOf(info.Extension);

		// fix for upper extensions
		if (index == -1)
		{
			index = 0;

			foreach (string extension in UNITY_EXTENSIONS)
			{
				if (info.Extension.ToLower() == extension.ToLower())
					break;

				index++;
			}
		}

		if (index >= 0 && index <= 13)
			return AssetType._3D_model;

		if (index >= 14 && index <= 23)
			return AssetType.Animation;

		if (index >= 24 && index <= 25)
			return AssetType.Assembly;

		if (index >= 26 && index <= 36)
			return AssetType.Audio;

		if (index >= 37 && index <= 38)
			return AssetType.Avatar;

		if (index == 39)
			return AssetType.Build_report;

		if (index >= 40 && index <= 41)
			return AssetType.Curves;

		if (index >= 42 && index <= 46)
			return AssetType.Font;

		if (index >= 47 && index <= 50)
			return AssetType.Independent_hardware_vendor;

		if (index == 51)
			return AssetType.Localization;

		if (index >= 52 && index <= 55)
			return AssetType.Material;

		if (index >= 56 && index <= 80)
			return AssetType.Plugin;

		if (index == 81)
			return AssetType.Prefab;

		if (index == 82)
			return AssetType.Preset;

		if (index >= 83 && index <= 84)
			return AssetType.Scene;

		if (index == 85)
		{
			if (File.ReadAllText(info.FullName).Contains("using UnityEditor;"))
				return AssetType.Editor_tool;

			return AssetType.Script;
		}

		if (index == 86)
			return AssetType.Scriptable_object;

		if (index >= 87 && index <= 96)
			return AssetType.Shader;

		if (index == 97)
			return AssetType.Substance;

		if (index >= 98 && index <= 99)
			return AssetType.Colors;

		if (index >= 100 && index <= 103)
			return AssetType.Terrain;

		if (index >= 104 && index <= 122)
			return AssetType.Texture;

		if (index == 123)
			return AssetType.GUI_skin;

		if (index >= 124 && index <= 135)
			return AssetType.Video;

		if (index >= 136 && index <= 145)
			return AssetType.Visual_effect;

		if (IsTextFile(info))
			return AssetType.Text;

		return AssetType.None;
	}

	private static bool IsTextFile(FileInfo info)
	{
		if (info.Length >= MAX_FILE_SIZE)
			return false;

		char[] fileChars = File.ReadAllText(info.FullName).ToCharArray(0, (int)Mathf.Min(100, info.Length));

		for (int i = 0; i < fileChars.Length; i++)
		{
			if (fileChars[i] >= 128)
				return false;
		}

		return true;
	}

	private static void ExtractClassNames(string guid, string path)
	{
		string scriptText = File.ReadAllText(path);
		string[] frags = scriptText.Split(" class ");

		for (int i = 1; i < frags.Length; i++)
		{
			string prevCheck = frags[i - 1];

			// invalidate "class" after ":"
			if (prevCheck.TrimEnd().Length == 0 || prevCheck.TrimEnd()[^1] == ':')
				continue;

			// long comment check
			string[] commentFrags = prevCheck.Split("/*");

			if (commentFrags.Length > 1 && !commentFrags[^1].Contains("*/"))
				continue;

			// line comment check
			string[] lineFrags = prevCheck.Split('\n');

			if (lineFrags[^1].Contains("//"))
				continue;

			// string check
			string[] sFrag = lineFrags[^1].Split('\"');
			int count = 0;

			if (sFrag.Length > 1)
			{
				for (int j = 0; j < sFrag.Length - 1; j++)
				{
					if (sFrag[j].Length == 0 || sFrag[j][^1] != '\\')
						count++;
				}
			}

			if (count % 2 != 0)
				continue;

			// extract class name
			string className = frags[i].TrimStart().Split(new char[] { ' ', '\'', '\n', '\r', ':', '<' })[0];

			if (!Instance.classToGuids.ContainsKey(className))
				Instance.classToGuids.Add(className, new List<string>());

			if (!Instance.classToGuids[className].Contains(guid))
				Instance.classToGuids[className].Add(guid);

			// check if custom editor
			if (prevCheck.Contains("CustomEditor"))
			{
				string inspectedType = prevCheck.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^2];
				inspectedType = inspectedType.Split(
					new string[] { "CustomEditor", "(", ")", "[", "]", "typeof", "\"" },
					StringSplitOptions.RemoveEmptyEntries
				)[0];

				Instance.classToEditor.Add(inspectedType, className);
			}
		}
	}

	private static void ManageFile(string path, string guid)
	{
		FileInfo file = new FileInfo(path);
		AssetType type = GetAssetType(file);

		// check asset bundle
		foreach (string line in File.ReadAllLines(path + ".meta"))
		{
			if (!line.Contains("assetBundleName: "))
				continue;

			string assetBudleName = line.Split("assetBundleName: ")[1];

			if (assetBudleName.Length > 0)
				AddAssetReference(ASSET_BUNDLE_FLAG + assetBudleName.TrimEnd('\n'), guid);

			break;
		}

		// file specific check
		if (type == AssetType.Scene)
			ManageSceneFile(guid, path, file);

		if (type == AssetType.Assembly)
			ManageJSONFile(guid, path, file);

		if (type == AssetType.Script || type == AssetType.Editor_tool)
			ManageScriptFile(guid, path);

		if (YAML_INDEXES.Contains((int)type) && ((int)type != 9 || file.Extension == UNITY_EXTENSIONS[42]))
			ManageYAMLFile(guid, path, file);
	}

	private static void ManageSceneFile(string guid, string path, FileInfo file)
	{
		// check build settings
		int index = 0;

		while (index < SceneManager.sceneCount)
		{
			if (SceneManager.GetSceneAt(index).path == path)
				AddAssetReference(BUILD_SETTINGS_FLAG, guid);

			index++;
		}

		// check guids in scene
		ManageYAMLFile(guid, path, file);
	}

	private static void ManageJSONFile(string guid, string path, FileInfo file)
	{
		// get refs
		foreach (string line in File.ReadAllLines(path))
		{
			if (!line.Contains(GUID_FLAG.ToUpper()))
				continue;

			string currentGuid = line.Split(GUID_FLAG.ToUpper())[1].Split('\"')[0].Trim();

			if (Instance.results.guidToPath.ContainsKey(currentGuid))
				AddAssetReference(guid, currentGuid);
		}

		// script refs to assembly
		foreach (string scriptPath in GetAllScriptsInAssembly(file, file.Directory))
		{
			string scriptGuid = GetAssetGUID(scriptPath);

			if (Instance.results.guidToPath.ContainsKey(scriptGuid))
			{
				AddAssetReference(scriptGuid, guid);
			}
			else
			{
				Debug.LogError("Script was not detected during indexing or not compiled by Unity, this is a very critical error. Skipping.");
			}
		}
	}

	private static void ManageScriptFile(string guid, string path)
	{
		// list of classes to ignore
		List<string> declaredClasses = new List<string>();

		foreach (KeyValuePair<string, List<string>> pair in Instance.classToGuids)
		{
			if (pair.Value.Contains(guid))
			{
				declaredClasses.Add(pair.Key);

				// link to custom editor
				if (Instance.classToEditor.ContainsKey(pair.Key))
				{
					foreach (string editorGuid in Instance.classToGuids[Instance.classToEditor[pair.Key]])
						AddAssetReference(guid, editorGuid);
				}
			}
		}

		// TODO : Same name classes are getting detected (including subclasses)
		// I'm not sure how I could fix that without making the tool understand C# fully...

		// detect class names
		List<string> scriptLines = new List<string>(File.ReadAllLines(path));
		bool inComment = false;

		for (int i = 0; i < scriptLines.Count; i++)
		{
			string line = scriptLines[i];

			// skip comment line (to go faster)
			if (line.TrimStart().StartsWith("//"))
				continue;

			int startIndex;
			int endIndex;

			if (!inComment)
			{
				// detect if call is valid
				if (line.Contains("Resources.Load"))
				{
					string[] resFrags = line.Split("Resources.Load");
					int stringOpenCount = 0;
					bool inLocalComment = false;
					bool inString = false;

					for (int j = 0; j < resFrags.Length - 1; j++)
					{
						// check inline long comment
						string frag = resFrags[j];
						startIndex = frag.LastIndexOf("/*");
						endIndex = frag.LastIndexOf("*/");

						if (startIndex > endIndex)
							inLocalComment = true;
						else if (endIndex > startIndex)
							inLocalComment = false;

						if (inLocalComment)
							continue;

						// check line comment
						if (frag.Contains("//"))
							break;

						if (frag.Contains('"'))
						{
							string[] sFrags = frag.Split('"');

							for (int h = 0; h < sFrags.Length - 1; h++)
							{
								if (sFrags[h][^1] != '\\')
									stringOpenCount++;
							}

							if (stringOpenCount % 2 != 0)
							{
								inString = true;
								continue;
							}
							else
								inString = false;
						}

						if (!inString)
						{
							for (int h = 1; h < resFrags.Length; h++)
							{
								string resPath = resFrags[h].Split("(")[1].Split(")")[0];

								if (resPath.Contains("\""))
									FindResourcesReference(resPath.Split('"')[1], guid);
							}
						}
					}
				}

				// detect if class name is valid
				foreach (string className in Instance.classToGuids.Keys)
				{
					// skip current declared class
					if (declaredClasses.Contains(className))
						continue;

					if (line.Contains(className))
					{
						string[] frags = line.Split(className);
						int stringOpenCount = 0;
						bool stopNow = false;
						bool inLocalComment = false;
						bool inString = false;

						for (int j = 0; j < frags.Length - 1; j++)
						{
							// check inline long comment
							string frag = frags[j];
							startIndex = frag.LastIndexOf("/*");
							endIndex = frag.LastIndexOf("*/");

							if (startIndex > endIndex)
								inLocalComment = true;
							else if (endIndex > startIndex)
								inLocalComment = false;

							if (inLocalComment)
								continue;

							// check line comment
							if (frags[j].Contains("//"))
							{
								stopNow = true;
								break;
							}

							// check in string
							if (frag.Contains('"'))
							{
								string[] sFrags = frag.Split('"', StringSplitOptions.RemoveEmptyEntries);

								for (int h = 0; h < sFrags.Length - 1; h++)
								{
									if (sFrags[h][^1] != '\\')
										stringOpenCount++;
								}

								if (stringOpenCount % 2 != 0)
								{
									inString = true;
									continue;
								}
								else
									inString = false;
							}

							if (!inString)
							{
								char prevChar = frag[^1];
								char nextChar = frags[j + 1].Length == 0 ? ' ' : frags[j + 1][0];
								bool valid = true;

								if (IsValidInClassName(prevChar) || IsValidInClassName(nextChar))
									valid = false;

								if (valid)
								{
									foreach (string refGuid in Instance.classToGuids[className])
										AddAssetReference(guid, refGuid);
								}
							}
						}

						if (stopNow)
							continue;
					}
				}
			}

			// check long comments
			startIndex = line.LastIndexOf("/*");
			endIndex = line.LastIndexOf("*/");

			if (startIndex > endIndex)
				inComment = true;
			else if (endIndex > startIndex)
				inComment = false;
		}
	}

	private static void ManageYAMLFile(string guid, string path, FileInfo file)
	{
		foreach (string line in File.ReadAllLines(path))
		{
			if (!line.Contains(GUID_FLAG))
				continue;

			string refGuid = line.Split(GUID_FLAG)[1].Split(',')[0].Replace("\\\"", "").Trim();

			if (Instance.results.guidToPath.ContainsKey(refGuid))
				AddAssetReference(guid, refGuid);
		}
	}

	private static void AddAssetReference(string guid, string refGuid)
	{
		// references
		if (!Instance.guidToReferences.ContainsKey(guid))
			Instance.guidToReferences.Add(guid, new List<string>());

		if (!Instance.guidToReferences[guid].Contains(refGuid))
			Instance.guidToReferences[guid].Add(refGuid);

		// sources
		if (!Instance.results.guidToSources.ContainsKey(refGuid))
			Instance.results.guidToSources.Add(refGuid, new List<string>());

		if (!Instance.results.guidToSources[refGuid].Contains(guid))
			Instance.results.guidToSources[refGuid].Add(guid);
	}

	private static void FindResourcesReference(string path, string guid)
	{
		// check resources paths list
		foreach (KeyValuePair<string, string> asset in Instance.resPathToGuid)
		{
			// key = path / value = guid
			string afterResPath = asset.Key.Split("Resources/")[1];
			afterResPath = afterResPath.Replace(new FileInfo(afterResPath).Extension, "");

			if (afterResPath.Replace("\\", "/") == path.Replace("\\", "/"))
			{
				// only ref first match (you shouldn't have multiple res with same path)
				AddAssetReference(guid, asset.Value);
				break;
			}
		}
	}

	private static bool IsValidInClassName(char letter)
	{
		return (letter >= 0 && letter <= 9) || // numbers
			(letter >= 65 && letter <= 90) || // A - Z
			letter == 95 || // _
			(letter >= 97 && letter <= 122); // a - z
	}

	private static List<string> GetAllScriptsInAssembly(FileInfo baseAssembly, DirectoryInfo baseDir)
	{
		List<string> filePaths = new List<string>();

		foreach (FileInfo file in baseDir.GetFiles())
		{
			// we cut here
			if (file.Extension == UNITY_EXTENSIONS[24] && file != baseAssembly)
				return new List<string>();

			// add script
			if (file.Extension == UNITY_EXTENSIONS[85])
				filePaths.Add(ConvertToProjectPath(file.FullName));
		}

		foreach (DirectoryInfo directory in baseDir.GetDirectories())
			filePaths.AddRange(GetAllScriptsInAssembly(baseAssembly, directory));

		return filePaths;
	}

	private static string ConvertToProjectPath(string fullPath)
	{
		return fullPath.Replace(Application.dataPath.Replace("/", "\\"), "Assets");
	}

	private string CleanAssetType(AssetType type) => type.ToString().Replace('_', ' ');

	private void GenerateIfNeeded()
	{
		if (titleStyle == null)
			titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

		if (rightStyle == null)
			rightStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };

		if (textStyle == null)
		{
			textStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleLeft,
				richText = true,
				wordWrap = true
			};
		}

		if (buttonStyle == null)
			buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };

		if (centerStyle == null)
			centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
	}

	private void CenterDisplay(Action callback)
	{
		EditorGUILayout.BeginHorizontal();
		{
			EditorGUILayout.Space();
			callback?.Invoke();
			EditorGUILayout.Space();
		}
		EditorGUILayout.EndHorizontal();
	}

	private void GUIDivider() => EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

	private static void CancelOperations()
	{
		if (Instance == null)
			return;

		Instance.cancelFlag = true;
		Instance.status = AnalysisStatus.Before;
	}

	private static bool CheckInterruption()
	{
		if (Instance == null)
		{
			Instance.status = AnalysisStatus.Before;
			EditorApplication.update -= AsyncAssetIndexing;
			EditorApplication.update -= AsyncAnalysis;
			EditorApplication.update -= AsyncRefAnalysis;
		}

		if (Instance.cancelFlag)
			CancelOperations();

		return Instance.cancelFlag || Instance == null;
	}

	private void DisplayColored(Action callback, Color color)
	{
		GUI.color = color;
		callback?.Invoke();
		GUI.color = Color.white;
	}

	private static void DereferenceAsset(string guid)
	{
		if (Instance.results.guidToPath.ContainsKey(guid))
			Instance.results.guidToPath.Remove(guid);

		if (Instance.results.guidToSources.ContainsKey(guid))
		{
			Instance.results.guidToSources.Remove(guid);

			foreach (KeyValuePair<string, List<string>> asset in Instance.results.guidToSources)
			{
				if (asset.Value.Contains(guid))
					asset.Value.Remove(guid);
			}
		}

		if (Instance.results.guidToRefStatus.ContainsKey(guid))
			Instance.results.guidToRefStatus.Remove(guid);

		if (Instance.classToGuids != null)
		{
			foreach (KeyValuePair<string, List<string>> asset in Instance.classToGuids)
			{
				if (asset.Value.Contains(guid))
					asset.Value.Remove(guid);
			}
		}

		if (Instance.guidToReferences != null)
		{
			if (Instance.guidToReferences.ContainsKey(guid))
				Instance.guidToReferences.Remove(guid);

			foreach (KeyValuePair<string, List<string>> asset in Instance.guidToReferences)
			{
				if (asset.Value.Contains(guid))
					asset.Value.Remove(guid);
			}
		}

		List<KeyValuePair<string, string>> selected = Instance.selectedRefResults.FindAll(x => x.Key == guid);
		selected.ForEach(x => Instance.selectedRefResults.Remove(x));
		selected.ForEach(x => Instance.selectedSearchResults.Remove(x));
		selected.ForEach(x => Instance.selectedSortResults.Remove(x));
		selected.ForEach(x => Instance.selectedPageResults.Remove(x));

		if (Instance.guidToFoldoutStatus.ContainsKey(guid))
			Instance.guidToFoldoutStatus.Remove(guid);

		if (Instance.guidToSelectedStatus.ContainsKey(guid))
			Instance.guidToSelectedStatus.Remove(guid);
	}

	private static void MoveAsset(string originPath, string newPath)
	{
		if (!File.Exists(originPath))
		{
			Debug.LogWarning("Couldn't find the asset you're trying to remove. You probably need to run a new project analysis. Skipping file.");
			return;
		}

		string path = originPath.Replace("\\", "/").Replace(Application.dataPath.Replace("\\", "/"), "");
		List<string> dirs = new List<string>(path.Split("/", StringSplitOptions.RemoveEmptyEntries));
		string currentDir = null;
		bool isRecovery = dirs.Contains(RECOVERY_DIR);

		if (isRecovery)
			dirs.RemoveAt(0);

		for (int i = 0; i < dirs.Count - 1; i++)
		{
			if (currentDir != null)
				currentDir = Path.Combine(currentDir, dirs[i]);
			else
				currentDir = dirs[i];

			string fullPath = Path.Combine(isRecovery ? Application.dataPath : recoveryDir, currentDir);

			if (!Directory.Exists(Path.GetFullPath(fullPath)))
				Directory.CreateDirectory(Path.GetFullPath(fullPath));
		}

		if (File.Exists(newPath))
			File.Delete(newPath);

		File.Move(originPath, newPath);
	}

	private List<string> GetFilesRecursive(string sourceDir)
	{
		List<string> files = new List<string>(Directory.GetFiles(sourceDir));

		foreach (string dir in Directory.GetDirectories(sourceDir))
			files.AddRange(GetFilesRecursive(dir));

		return files;
	}

	private static void GenerateSelectionDictionary()
	{
		Instance.guidToSelectedStatus = new Dictionary<string, bool>();
		Instance.guidToFoldoutStatus = new Dictionary<string, bool>();

		foreach (KeyValuePair<string, string> pair in Instance.results.guidToPath)
		{
			Instance.guidToSelectedStatus.Add(pair.Key, false);
			Instance.guidToFoldoutStatus.Add(pair.Key, false);
		}
	}

	private static void RegisterAssetRef(List<string> list, string guid)
	{
		Instance.results.guidToRefStatus[guid] = true;

		if (Instance.guidToReferences.ContainsKey(guid))
		{
			Instance.guidToReferences[guid].ForEach(item =>
			{
				if (!list.Contains(item))
					list.Add(item);
			});
		}
	}

	private void AsyncLoadingBar(string title, float progress)
	{
		EditorGUILayout.LabelField(title, centerStyle);
		EditorGUILayout.Space();

		Rect rect = EditorGUILayout.BeginVertical();
		EditorGUI.ProgressBar(rect, progress, title);
		EditorGUILayout.EndVertical();
	}

	/// <summary>Save class for results of the Unused Asset Finder</summary>
	[Serializable]
	private class AnalysisResults
	{
		// usable part
		public Dictionary<string, string> guidToPath;
		public Dictionary<string, List<string>> guidToSources;
		public Dictionary<string, bool> guidToRefStatus;

		// serialization part
		[SerializeField]
		private List<string> guidToPathKeys;
		[SerializeField]
		private List<string> guidToPathValues;

		[SerializeField]
		private List<string> guidToSourcesKeys;
		[SerializeField]
		private List<Values> guidToSourcesValues;

		[SerializeField]
		private List<string> guidToRefStatusKeys;
		[SerializeField]
		private List<bool> guidToRefStatusValues;

		public AnalysisResults()
		{
			guidToPath = new Dictionary<string, string>();
			guidToSources = new Dictionary<string, List<string>>();
			guidToRefStatus = new Dictionary<string, bool>();
		}

		public void PreSerialization()
		{
			guidToPathKeys = new List<string>();
			guidToPathValues = new List<string>();

			foreach (KeyValuePair<string, string> pair in guidToPath)
			{
				guidToPathKeys.Add(pair.Key);
				guidToPathValues.Add(pair.Value);
			}

			guidToSourcesKeys = new List<string>();
			guidToSourcesValues = new List<Values>();

			foreach (KeyValuePair<string, List<string>> pair in guidToSources)
			{
				guidToSourcesKeys.Add(pair.Key);
				guidToSourcesValues.Add(new Values(pair.Value));
			}

			guidToRefStatusKeys = new List<string>();
			guidToRefStatusValues = new List<bool>();

			foreach (KeyValuePair<string, bool> pair in guidToRefStatus)
			{
				guidToRefStatusKeys.Add(pair.Key);
				guidToRefStatusValues.Add(pair.Value);
			}
		}

		public void PostSerialization()
		{
			guidToPath = new Dictionary<string, string>();

			for (int i = 0; i < guidToPathKeys.Count; i++)
				guidToPath.Add(guidToPathKeys[i], guidToPathValues[i]);

			guidToSources = new Dictionary<string, List<string>>();

			for (int i = 0; i < guidToSourcesKeys.Count; i++)
			{
				guidToSources.Add(guidToSourcesKeys[i], guidToSourcesValues[i].data);
			}

			guidToRefStatus = new Dictionary<string, bool>();

			for (int i = 0; i < guidToRefStatusKeys.Count; i++)
				guidToRefStatus.Add(guidToRefStatusKeys[i], guidToRefStatusValues[i]);
		}

		[Serializable]
		public class Values
		{
			public List<string> data;

			public Values(List<string> data) => this.data = data;
		}
	}

	/// <summary>Preview popup for the Unused Assets Finder</summary>
	private class PreviewPopup : EditorWindow
	{
		private const float minPadding = 10;

		private static PreviewPopup _instance;
		private static PreviewPopup Instance
		{
			get
			{
				if (_instance == null)
					SpawnWindowIfNeeded();

				return _instance;
			}
		}

		private GUIStyle textStyle;

		private System.Object obj;
		private AssetType type;
		private string path;
		private Texture display;
		private Vector2 scroll;
		private Editor SO_Editor;

		// Main flow methods
		private static void SpawnWindowIfNeeded()
		{
			bool needInit = false;

			if (_instance == null)
				needInit = true;

			_instance = GetWindow<PreviewPopup>();

			if (needInit)
			{
				_instance.titleContent = new GUIContent("Unused Assets Finder : Preview");
				_instance.minSize = new Vector2(250, 250);
			}
		}

		public static void Preview(string path, AssetType type)
		{
			SpawnWindowIfNeeded();

			Instance.obj = AssetDatabase.LoadAssetAtPath(path, GetType(type));
			Instance.type = type;
			Instance.path = path;

			Instance.display = null;
			Instance.SO_Editor = null;
		}

		private void OnGUI()
		{
			GenerateIfNeeded();

			if (display == null)
			{
				switch (type)
				{
					case AssetType.Texture:
						display = obj as Texture;
						break;

					case AssetType.Script:
					case AssetType.Editor_tool:
					case AssetType.Shader:
					case AssetType.Text:
						if (type == AssetType.Shader && new FileInfo(path).Extension == UNITY_EXTENSIONS[95])
							EditorGUILayout.HelpBox("No preview for " + type + " assets.", MessageType.Warning);
						else
						{
							scroll = EditorGUILayout.BeginScrollView(scroll);
							EditorGUILayout.LabelField(File.ReadAllText(path), textStyle);
							EditorGUILayout.EndScrollView();
						}
						break;

					case AssetType.Prefab:
					case AssetType.Material:
						display = AssetPreview.GetAssetPreview(obj as UnityEngine.Object);
						break;

					case AssetType._3D_model:
						GameObject gameObj = obj as GameObject;
						MeshFilter filter = gameObj.GetComponent<MeshFilter>();
						SkinnedMeshRenderer skin = gameObj.GetComponent<SkinnedMeshRenderer>();

						if (filter != null || skin != null)
						{
							Mesh mesh = filter != null ? filter.sharedMesh : skin.sharedMesh;
							display = AssetPreview.GetAssetPreview(mesh);
						}
						else
							EditorGUILayout.HelpBox("No mesh found in this asset.", MessageType.Error);
						break;

					case AssetType.Scriptable_object:
						if (SO_Editor == null)
							SO_Editor = Editor.CreateEditor(obj as ScriptableObject);

						if (SO_Editor != null)
						{
							scroll = EditorGUILayout.BeginScrollView(scroll);
							SO_Editor.OnInspectorGUI();
							EditorGUILayout.EndScrollView();
						}
						else
							EditorGUILayout.HelpBox("No preview for " + type + " assets.", MessageType.Warning);

						break;

					default:
						EditorGUILayout.HelpBox("No preview for " + type + " assets.", MessageType.Warning);
						break;
				}
			}
			else
				RenderTexture(display);
		}

		// Utility methods
		private static Type GetType(AssetType assetType)
		{
			switch (assetType)
			{
				case AssetType.Texture:
					return typeof(Texture);

				case AssetType.Prefab:
					return typeof(GameObject);

				case AssetType.Material:
					return typeof(Material);

				case AssetType._3D_model:
					return typeof(GameObject);

				case AssetType.Scriptable_object:
					return typeof(ScriptableObject);

				default:
					return null;
			}
		}

		private void GenerateIfNeeded()
		{
			if (textStyle == null)
				textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };
		}

		private void RenderTexture(Texture texture)
		{
			if (texture == null)
			{
				EditorGUILayout.HelpBox("Couldn't render asset.", MessageType.Error);
				return;
			}

			EditorGUI.DrawPreviewTexture(GetDisplayRect(), texture);
		}

		private Rect GetDisplayRect()
		{
			float minSize = Mathf.Min(Instance.position.width, Instance.position.height);
			float sizeDif = Instance.position.width - Instance.position.height;
			float verticalPadding = sizeDif < 0 ? -sizeDif / 2 : 0;
			float horizontalPadding = sizeDif > 0 ? sizeDif / 2 : 0;

			return new Rect(
				new Vector2(minPadding + horizontalPadding, minPadding + verticalPadding),
				Vector2.one * (minSize - minPadding * 2)
			);
		}
	}
}