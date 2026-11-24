using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

// =====================================================================
// AppView.cs
// Liga o UXML (UI_Idosos.uxml) ao AppViewModel (jogo de funcionário de mercado).
//  - Textos, largura da barra e valores: DATA BINDING (SetBinding).
//  - Troca de telas, painéis e classes USS, e botões ativos/inativos: reação ao
//    evento Changed do ViewModel (classes não são bindáveis).
//  - Cliques: Button.clicked chama métodos do ViewModel.
// Requer Unity 6 (6000.0) ou superior.
// =====================================================================

[RequireComponent(typeof(UIDocument))]
public class AppView : MonoBehaviour
{
    [Header("Fluxo de telas (versões dos seus desenhos)")]
    [SerializeField] FlowVersion flow = FlowVersion.A;

    [Header("Áudio (ligue ao seu mixer ou AudioSources)")]
    [SerializeField] UnityEvent<bool> onSoundChanged;
    [SerializeField] UnityEvent<bool> onMusicChanged;

    AppViewModel _vm;
    VisualElement _app;

    static readonly Dictionary<ScreenId, string> ScreenNames = new()
    {
        { ScreenId.Loading, "screen-loading" },
        { ScreenId.Menu, "screen-menu" },
        { ScreenId.Profile, "screen-profile" },
        { ScreenId.Settings, "screen-settings" },
        { ScreenId.Market, "screen-market" },
        { ScreenId.Levels, "screen-levels" },
        { ScreenId.LevelInfo, "screen-level-info" },
        { ScreenId.Game, "screen-game" },
        { ScreenId.Result, "screen-result" },
    };

    // Primeiro botão a receber foco em cada tela (teclado e controle)
    static readonly Dictionary<ScreenId, string> FirstFocus = new()
    {
        { ScreenId.Menu, "btn-play" },
        { ScreenId.Profile, "btn-back-profile" },
        { ScreenId.Settings, "btn-back-settings" },
        { ScreenId.Market, "btn-back-market" },
        { ScreenId.Levels, "btn-back-levels" },
        { ScreenId.LevelInfo, "btn-start-level" },
        { ScreenId.Game, "btn-pause" },
        { ScreenId.Result, "btn-again" },
    };

    // =====================================================================
    // Ciclo de vida
    // O componente UIDocument deve ficar ACIMA deste script no Inspector.
    // =====================================================================
    void OnEnable()
    {
        var doc = GetComponent<UIDocument>();
        if (doc.visualTreeAsset == null)
        {
            Debug.LogError("UIDocument sem 'Source Asset'. Arraste o UI_Idosos.uxml para esse campo.");
            return;
        }

        var root = doc.rootVisualElement;
        _app = root.Q("app-root");
        if (_app == null)
        {
            Debug.LogError("Elemento 'app-root' não encontrado. O UXML pode ter falhado ao importar: veja o primeiro erro vermelho no Console.");
            return;
        }

        try
        {
            _vm = new AppViewModel(flow);
            _vm.Delay = (ms, action) => _app.schedule.Execute(action).StartingIn(ms);
            _vm.QuitRequested += QuitApplication;
            _vm.Changed += OnViewModelChanged;

            // O data source é herdado por todos os elementos filhos.
            _app.dataSource = _vm;

            _app.EnableInClassList("version-a", flow == FlowVersion.A);
            _app.EnableInClassList("version-b", flow == FlowVersion.B);

            CreateBindings();
            WireButtons();
            ApplyEverything();
            StartLoading();
            Debug.Log("[AppView] Interface iniciada.");
        }
        catch (Exception e)
        {
            // Mostra exatamente em qual passo a interface falhou.
            Debug.LogError("Falha ao iniciar a interface (veja a exceção abaixo).");
            Debug.LogException(e);
        }
    }

    void OnDisable()
    {
        if (_vm == null) return;
        _vm.QuitRequested -= QuitApplication;
        _vm.Changed -= OnViewModelChanged;
        _vm.Save();
    }

    // =====================================================================
    // 1. DATA BINDINGS (ViewModel -> elementos)
    // =====================================================================
    void CreateBindings()
    {
        // Carregamento
        BindText("loading-percent", nameof(AppViewModel.LoadingPercentText));
        Bind("progress-fill", "style.width", nameof(AppViewModel.LoadingBarWidth));

        // Menu e perfil
        BindText("menu-greeting", nameof(AppViewModel.GreetingText));
        BindText("menu-coins", nameof(AppViewModel.CoinsText));
        BindText("menu-customers", nameof(AppViewModel.CustomersText));
        BindText("avatar-letter", nameof(AppViewModel.AvatarLetter));
        BindText("profile-name", nameof(AppViewModel.PlayerName));
        BindText("profile-coins", nameof(AppViewModel.CoinsText));
        BindText("profile-customers", nameof(AppViewModel.CustomersText));
        BindText("profile-role", nameof(AppViewModel.RoleText));
        BindText("profile-levels", nameof(AppViewModel.LevelsProgressText));

        // Configurações
        BindText("btn-toggle-sound", nameof(AppViewModel.SoundText));
        BindText("btn-toggle-music", nameof(AppViewModel.MusicText));

        // Loja do funcionário
        BindText("market-coins", nameof(AppViewModel.CoinsText));
        BindText("market-message", nameof(AppViewModel.MarketMessage));
        for (int i = 0; i < _vm.ShopItems.Length; i++)
        {
            // Cada rótulo recebe o próprio ShopItem como data source.
            BindWithSource($"item-{i + 1}-name", nameof(ShopItem.Name), _vm.ShopItems[i]);
            BindWithSource($"item-{i + 1}-desc", nameof(ShopItem.Description), _vm.ShopItems[i]);
            BindWithSource($"item-{i + 1}-price", nameof(ShopItem.PriceText), _vm.ShopItems[i]);
        }

        // Fases
        BindText("level-info-title", nameof(AppViewModel.LevelInfoTitle));
        BindText("level-info-desc", nameof(AppViewModel.LevelInfoDesc));

        // Jogo: HUD e mensagem
        BindText("hud-coins", nameof(AppViewModel.CoinsText));
        BindText("hud-customers", nameof(AppViewModel.CustomersText));
        BindText("game-instruction", nameof(AppViewModel.GameInstruction));
        BindText("game-feedback", nameof(AppViewModel.Feedback));

        // Minigame: dinheiro
        BindText("money-question", nameof(AppViewModel.MoneyQuestion));
        BindText("money-answer-1", nameof(AppViewModel.MoneyAnswer1));
        BindText("money-answer-2", nameof(AppViewModel.MoneyAnswer2));
        BindText("money-answer-3", nameof(AppViewModel.MoneyAnswer3));

        // Minigame: código
        BindText("code-slot-1", nameof(AppViewModel.CodeSlot1));
        BindText("code-slot-2", nameof(AppViewModel.CodeSlot2));
        BindText("code-slot-3", nameof(AppViewModel.CodeSlot3));
        BindText("code-slot-4", nameof(AppViewModel.CodeSlot4));

        // Minigame: palavras
        BindText("words-hint", nameof(AppViewModel.WordsHint));
        BindText("word-slot-1", nameof(AppViewModel.WordSlot1));
        BindText("word-slot-2", nameof(AppViewModel.WordSlot2));
        BindText("word-slot-3", nameof(AppViewModel.WordSlot3));
        BindText("word-slot-4", nameof(AppViewModel.WordSlot4));
        BindText("tile-1", nameof(AppViewModel.Tile1));
        BindText("tile-2", nameof(AppViewModel.Tile2));
        BindText("tile-3", nameof(AppViewModel.Tile3));
        BindText("tile-4", nameof(AppViewModel.Tile4));

        // Resultado
        BindText("result-title", nameof(AppViewModel.ResultTitle));
        BindText("result-stars", nameof(AppViewModel.ResultStars));
        BindText("result-coins", nameof(AppViewModel.ResultCoins));
        BindText("result-customers", nameof(AppViewModel.ResultCustomers));
    }

    void BindText(string elementName, string path) => Bind(elementName, "text", path);

    void Bind(string elementName, string bindingId, string path)
    {
        var element = Find(elementName);
        element?.SetBinding(bindingId, new DataBinding
        {
            dataSourcePath = new PropertyPath(path),
            bindingMode = BindingMode.ToTarget,
        });
    }

    void BindWithSource(string elementName, string path, object source)
    {
        var element = Find(elementName);
        if (element == null) return;
        element.dataSource = source;
        element.SetBinding("text", new DataBinding
        {
            dataSourcePath = new PropertyPath(path),
            bindingMode = BindingMode.ToTarget,
        });
    }

    VisualElement Find(string name)
    {
        var element = _app.Q(name);
        if (element == null) Debug.LogWarning($"Elemento '{name}' não encontrado no UXML.");
        return element;
    }

    // =====================================================================
    // 2. CLIQUES (elementos -> ViewModel)
    // =====================================================================
    void WireButtons()
    {
        // Menu
        Click("btn-play", _vm.OpenPlay);
        Click("btn-market", () => _vm.Go(ScreenId.Market));
        Click("btn-profile", () => _vm.Go(ScreenId.Profile));
        Click("btn-settings", () => _vm.Go(ScreenId.Settings));

        // Voltar
        Click("btn-back-profile", () => _vm.Go(ScreenId.Menu));
        Click("btn-back-settings", () => _vm.Go(ScreenId.Menu));
        Click("btn-back-market", () => _vm.Go(ScreenId.Menu));
        Click("btn-back-levels", () => _vm.Go(ScreenId.Menu));
        Click("btn-back-level-info", () => _vm.Go(ScreenId.Levels));

        // Configurações
        Click("btn-toggle-sound", _vm.ToggleSound);
        Click("btn-toggle-music", _vm.ToggleMusic);
        Click("btn-text-md", () => _vm.SetTextScale(TextScale.Normal));
        Click("btn-text-lg", () => _vm.SetTextScale(TextScale.Large));
        Click("btn-text-xl", () => _vm.SetTextScale(TextScale.Huge));
        Click("btn-quit", () => _vm.AskQuit(false));

        // Mercado
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Click($"btn-buy-{i + 1}", () => _vm.Buy(index));
        }

        // Fases (versão A)
        for (int n = 1; n <= AppViewModel.TotalLevels; n++)
        {
            int number = n;
            Click($"level-{n}", () => _vm.SelectLevel(number));
        }
        Click("btn-start-level", _vm.StartMatch);

        // Jogo
        Click("btn-pause", _vm.PauseGame);

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Click($"money-answer-{i + 1}", () => _vm.AnswerMoney(index));
        }

        for (int d = 0; d <= 9; d++)
        {
            int digit = d;
            Click($"key-{d}", () => _vm.PressKey(digit));
        }
        Click("key-erase", _vm.EraseCode);
        Click("key-ok", _vm.ConfirmCode);

        for (int i = 0; i < 4; i++)
        {
            int index = i;
            Click($"tile-{i + 1}", () => _vm.PressTile(index));
        }
        Click("words-erase", _vm.EraseWord);

        // Resultado
        Click("btn-again", _vm.PlayAgain);
        Click("btn-next-level", _vm.NextLevel);
        Click("btn-result-menu", () => _vm.Go(ScreenId.Menu));

        // Pausa
        Click("btn-resume", _vm.ResumeGame);
        Click("btn-restart", _vm.StartMatch);
        Click("btn-to-levels", () => _vm.Go(ScreenId.Levels));
        Click("btn-to-menu", () => _vm.Go(ScreenId.Menu));
        Click("btn-quit-game", () => _vm.AskQuit(true));

        // Confirmar saída
        Click("btn-quit-no", _vm.CancelQuit);
        Click("btn-quit-yes", _vm.ConfirmQuit);
    }

    void Click(string buttonName, Action action)
    {
        var button = _app.Q<Button>(buttonName);
        if (button == null)
        {
            Debug.LogWarning($"Botão '{buttonName}' não encontrado no UXML.");
            return;
        }
        button.clicked += action;
    }

    // =====================================================================
    // 3. REAÇÕES A MUDANÇAS (classes USS, foco, botões ativos)
    // =====================================================================
    void OnViewModelChanged(string property)
    {
        switch (property)
        {
            case nameof(AppViewModel.Screen): ApplyScreen(); break;
            case nameof(AppViewModel.Overlay): ApplyOverlay(); break;
            case nameof(AppViewModel.TextScale): ApplyTextScale(); break;
            case nameof(AppViewModel.SoundOn): ApplyToggles(); onSoundChanged?.Invoke(_vm.SoundOn); break;
            case nameof(AppViewModel.MusicOn): ApplyToggles(); onMusicChanged?.Invoke(_vm.MusicOn); break;
            case nameof(AppViewModel.LevelsCompleted): ApplyLevels(); break;
            case nameof(AppViewModel.SelectedLevel): ApplyResultButtons(); break;
            case nameof(AppViewModel.CurrentMiniGame): ApplyMiniGame(); break;
            case nameof(AppViewModel.FeedbackKind): ApplyFeedback(); break;
            case nameof(AppViewModel.TileUsedMask): ApplyTiles(); break;
        }
    }

    void ApplyEverything()
    {
        ApplyScreen();
        ApplyOverlay();
        ApplyTextScale();
        ApplyToggles();
        ApplyLevels();
        ApplyMiniGame();
        ApplyFeedback();
        ApplyTiles();
        ApplyResultButtons();
        onSoundChanged?.Invoke(_vm.SoundOn);
        onMusicChanged?.Invoke(_vm.MusicOn);
    }

    /// <summary>
    /// Mostra ou esconde um elemento. Além da classe USS, define o display direto no
    /// elemento: o UI Builder grava "display: none/flex" dentro do UXML (estilo inline),
    /// e um estilo inline vence qualquer regra de classe, o que deixava as telas travadas.
    /// </summary>
    static void SetShown(VisualElement element, bool shown, string className = "active")
    {
        if (element == null) return;
        element.EnableInClassList(className, shown);
        element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void ApplyScreen()
    {
        foreach (var pair in ScreenNames)
            SetShown(_app.Q(pair.Value), pair.Key == _vm.Screen);

        if (FirstFocus.TryGetValue(_vm.Screen, out var buttonName))
            FocusLater(buttonName);
    }

    void ApplyOverlay()
    {
        SetShown(_app.Q("overlay-pause"), _vm.Overlay == OverlayId.Pause);
        SetShown(_app.Q("overlay-quit"), _vm.Overlay == OverlayId.Quit);

        // Na janela de saída o foco começa em "Não" (opção segura).
        if (_vm.Overlay == OverlayId.Pause) FocusLater("btn-resume");
        else if (_vm.Overlay == OverlayId.Quit) FocusLater("btn-quit-no");
    }

    void ApplyTextScale()
    {
        _app.EnableInClassList("text-md", _vm.TextScale == TextScale.Normal);
        _app.EnableInClassList("text-lg", _vm.TextScale == TextScale.Large);
        _app.EnableInClassList("text-xl", _vm.TextScale == TextScale.Huge);

        SetSelected("btn-text-md", _vm.TextScale == TextScale.Normal);
        SetSelected("btn-text-lg", _vm.TextScale == TextScale.Large);
        SetSelected("btn-text-xl", _vm.TextScale == TextScale.Huge);
    }

    void SetSelected(string buttonName, bool selected)
    {
        var button = _app.Q(buttonName);
        button?.EnableInClassList("selected", selected);
        button?.EnableInClassList("btn-ghost", !selected);
    }

    void ApplyToggles()
    {
        SetToggle("btn-toggle-sound", _vm.SoundOn);
        SetToggle("btn-toggle-music", _vm.MusicOn);
    }

    void SetToggle(string buttonName, bool on)
    {
        var button = _app.Q(buttonName);
        button?.EnableInClassList("toggle-on", on);
        button?.EnableInClassList("toggle-off", !on);
    }

    void ApplyLevels()
    {
        for (int n = 1; n <= AppViewModel.TotalLevels; n++)
        {
            var card = _app.Q<Button>($"level-{n}");
            if (card == null) continue;

            bool done = n <= _vm.LevelsCompleted;
            bool open = n == _vm.LevelsCompleted + 1;
            bool locked = !done && !open;

            card.EnableInClassList("level-done", done);
            card.EnableInClassList("level-open", open);
            card.EnableInClassList("level-locked", locked);
            card.SetEnabled(!locked);

            var status = card.Q<Label>(className: "level-status");
            if (status != null) status.text = done ? "Concluído" : (open ? "Disponível" : "Bloqueado");
        }

        ApplyResultButtons();
    }

    void ApplyResultButtons()
    {
        _app.Q<Button>("btn-next-level")?.SetEnabled(_vm.SelectedLevel < AppViewModel.TotalLevels);
    }

    void ApplyMiniGame()
    {
        SetShown(_app.Q("minigame-money"), _vm.CurrentMiniGame == MiniGameId.Money);
        SetShown(_app.Q("minigame-code"), _vm.CurrentMiniGame == MiniGameId.Code);
        SetShown(_app.Q("minigame-words"), _vm.CurrentMiniGame == MiniGameId.Words);
    }

    void ApplyFeedback()
    {
        var label = _app.Q("game-feedback");
        if (label == null) return;
        SetShown(label, _vm.FeedbackKind != FeedbackKind.None, "show");
        label.EnableInClassList("feedback-ok", _vm.FeedbackKind == FeedbackKind.Ok);
        label.EnableInClassList("feedback-error", _vm.FeedbackKind == FeedbackKind.Error);
    }

    void ApplyTiles()
    {
        for (int i = 0; i < 4; i++)
        {
            bool used = (_vm.TileUsedMask & (1 << i)) != 0;
            _app.Q<Button>($"tile-{i + 1}")?.SetEnabled(!used);
        }
    }

    void FocusLater(string buttonName)
    {
        _app.schedule.Execute(() => _app.Q<Button>(buttonName)?.Focus()).StartingIn(50);
    }

    // =====================================================================
    // Carregamento (simulado) e saída
    // =====================================================================
    void StartLoading()
    {
        // Troque por progresso real, por exemplo AsyncOperation.progress.
        // Um único temporizador: sobe a barra e, ao chegar a 100%, abre o menu.
        _app.schedule.Execute(() =>
        {
            _vm.LoadingProgress += 0.02f;
            if (_vm.LoadingProgress >= 0.999f && _vm.Screen == ScreenId.Loading)
                _vm.FinishLoading();
        })
        .Every(40)
        .Until(() => _vm.Screen != ScreenId.Loading);
    }

    void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
