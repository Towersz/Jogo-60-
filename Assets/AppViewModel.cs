using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

// =====================================================================
// AppViewModel.cs
// Estado e regras do jogo "funcionário do mercado". Não conhece nenhum
// elemento visual.
// Cada propriedade marcada com [CreateProperty] pode ser ligada a um
// elemento do UXML por data binding (Unity 6 ou superior).
// Quando o valor muda, Notify() avisa a interface e ela se atualiza sozinha.
//
// Vocabulário do jogo
//   fase            -> dia de trabalho (turno)
//   3 minigames     -> 3 clientes por turno, cada um num posto:
//                      Caixa (dinheiro), Balança (código), Etiquetas (palavras)
//   vidas           -> clientes atendidos (só soma, nunca perde)
//   mercado (loja)  -> loja do funcionário, onde se gasta as moedas
// =====================================================================

public enum ScreenId { Loading, Menu, Profile, Settings, Market, Levels, LevelInfo, Game, Result }
public enum OverlayId { None, Pause, Quit }
public enum FlowVersion { A, B }
public enum TextScale { Normal, Large, Huge }
public enum MiniGameId { Money, Code, Words }
public enum FeedbackKind { None, Ok, Error }

/// <summary>Item da loja do funcionário. Cada cartão usa um ShopItem como data source próprio.</summary>
public class ShopItem
{
    public int Price { get; }

    [CreateProperty] public string Name { get; }
    [CreateProperty] public string Description { get; }
    [CreateProperty] public string PriceText => $"{Price} moedas";

    public ShopItem(string name, string description, int price)
    {
        Name = name;
        Description = description;
        Price = price;
    }
}

public class AppViewModel : INotifyBindablePropertyChanged
{
    public const int TotalLevels = 6;
    const int MiniGamesPerMatch = 3;

    // Etiquetas de preço: nomes de produtos com 4 letras (a interface tem 4 casas)
    static readonly (string word, string hint)[] WordBank =
    {
        ("OVOS", "Vêm da galinha"),
        ("UVAS", "Frutinhas em cachos"),
        ("SUCO", "Bebida feita de fruta"),
        ("BOLO", "Doce da padaria"),
        ("PERA", "Fruta macia e doce"),
        ("SOPA", "Prato quente de colher"),
    };

    // Produtos pesados na balança
    static readonly string[] Products = { "a banana", "o arroz", "o feijão", "o café", "o queijo", "o tomate" };

    // ---------- Infraestrutura de notificação ----------
    /// <summary>Usado pelo sistema de data binding do UI Toolkit.</summary>
    public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

    /// <summary>
    /// Aviso simples, com o nome da propriedade em texto puro. A View usa este evento
    /// para trocar telas e classes USS, sem depender do tipo BindingId.
    /// </summary>
    public event Action<string> Changed;

    public event Action QuitRequested;

    /// <summary>Preenchido pela View: executa uma ação depois de X milissegundos.</summary>
    public Action<long, Action> Delay = (ms, action) => action();

    void Notify(string property)
    {
        // Um erro na reação da tela é mostrado no Console, mas não trava o resto do jogo.
        try { Changed?.Invoke(property); }
        catch (Exception e) { Debug.LogException(e); }

        propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
    }

    bool Set<T>(ref T field, T value, string property, params string[] dependents)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(property);
        foreach (var d in dependents) Notify(d);
        return true;
    }

    // ---------- Navegação ----------
    ScreenId _screen = ScreenId.Loading;
    OverlayId _overlay = OverlayId.None;
    public ScreenId Screen { get => _screen; private set => Set(ref _screen, value, nameof(Screen)); }
    public OverlayId Overlay { get => _overlay; private set => Set(ref _overlay, value, nameof(Overlay)); }
    public FlowVersion Flow { get; }

    // ---------- Funcionário e economia ----------
    string _playerName = "Jogador";
    int _coins = 120;
    int _customers;
    int _levelsCompleted;
    int _selectedLevel = 1;

    [CreateProperty]
    public string PlayerName
    {
        get => _playerName;
        set => Set(ref _playerName, value, nameof(PlayerName), nameof(GreetingText), nameof(AvatarLetter));
    }

    [CreateProperty] public string GreetingText => $"Bom dia, {_playerName}!";
    [CreateProperty] public string AvatarLetter => string.IsNullOrEmpty(_playerName) ? "?" : _playerName.Substring(0, 1).ToUpper();

    /// <summary>Cargo no crachá: sobe conforme os dias de trabalho concluídos.</summary>
    [CreateProperty]
    public string RoleText
    {
        get
        {
            if (_levelsCompleted <= 0) return "Atendente em treinamento";
            if (_levelsCompleted <= 2) return "Atendente";
            if (_levelsCompleted <= 4) return "Operador de caixa";
            if (_levelsCompleted < TotalLevels) return "Supervisor de loja";
            return "Gerente do mercado";
        }
    }

    public int Coins
    {
        get => _coins;
        private set => Set(ref _coins, value, nameof(Coins), nameof(CoinsText));
    }

    public int Customers
    {
        get => _customers;
        private set => Set(ref _customers, value, nameof(Customers), nameof(CustomersText));
    }

    [CreateProperty] public string CoinsText => _coins.ToString();
    [CreateProperty] public string CustomersText => _customers.ToString();

    public int LevelsCompleted
    {
        get => _levelsCompleted;
        private set => Set(ref _levelsCompleted, value, nameof(LevelsCompleted), nameof(LevelsProgressText), nameof(RoleText));
    }

    [CreateProperty] public string LevelsProgressText => $"{_levelsCompleted} de {TotalLevels}";

    public int SelectedLevel
    {
        get => _selectedLevel;
        private set => Set(ref _selectedLevel, value, nameof(SelectedLevel), nameof(LevelInfoTitle), nameof(LevelInfoDesc));
    }

    [CreateProperty] public string LevelInfoTitle => $"Dia {_selectedLevel} de trabalho";

    [CreateProperty]
    public string LevelInfoDesc =>
        $"No dia {_selectedLevel} você atende 3 clientes, um de cada vez: no caixa, na balança e nas etiquetas. Leve o tempo que precisar.";

    // ---------- Carregamento ----------
    float _loading;

    public float LoadingProgress
    {
        get => _loading;
        set => Set(ref _loading, Mathf.Clamp01(value), nameof(LoadingProgress), nameof(LoadingBarWidth), nameof(LoadingPercentText));
    }

    [CreateProperty] public StyleLength LoadingBarWidth => new StyleLength(Length.Percent(_loading * 100f));
    [CreateProperty] public string LoadingPercentText => $"{Mathf.RoundToInt(_loading * 100f)}%";

    // ---------- Configurações ----------
    bool _soundOn = true;
    bool _musicOn = true;
    TextScale _textScale = TextScale.Normal;

    public bool SoundOn
    {
        get => _soundOn;
        private set { if (Set(ref _soundOn, value, nameof(SoundOn), nameof(SoundText))) Save(); }
    }

    public bool MusicOn
    {
        get => _musicOn;
        private set { if (Set(ref _musicOn, value, nameof(MusicOn), nameof(MusicText))) Save(); }
    }

    public TextScale TextScale
    {
        get => _textScale;
        private set { if (Set(ref _textScale, value, nameof(TextScale))) Save(); }
    }

    [CreateProperty] public string SoundText => _soundOn ? "Ligado" : "Desligado";
    [CreateProperty] public string MusicText => _musicOn ? "Ligada" : "Desligada";

    // ---------- Loja do funcionário ----------
    string _marketMessage = "";
    public readonly ShopItem[] ShopItems;

    [CreateProperty]
    public string MarketMessage
    {
        get => _marketMessage;
        private set => Set(ref _marketMessage, value, nameof(MarketMessage));
    }

    // ---------- Turno (partida) ----------
    int _step;
    int _mistakes;
    int _matchId;
    bool _locked;
    bool _quitFromPause;
    MiniGameId _current;
    string _station = "Caixa";
    string _instructionExtra = "";

    public MiniGameId CurrentMiniGame { get => _current; private set => Set(ref _current, value, nameof(CurrentMiniGame)); }

    [CreateProperty]
    public string GameInstruction => $"Cliente {_step + 1} de {MiniGamesPerMatch} ({_station}). {_instructionExtra}";

    // Feedback de acerto/erro
    string _feedback = "";
    FeedbackKind _feedbackKind = FeedbackKind.None;
    int _feedbackToken;

    [CreateProperty] public string Feedback { get => _feedback; private set => Set(ref _feedback, value, nameof(Feedback)); }
    public FeedbackKind FeedbackKind { get => _feedbackKind; private set => Set(ref _feedbackKind, value, nameof(FeedbackKind)); }

    // Posto: Caixa (troco e total da compra)
    string _moneyQuestion = "";
    readonly int[] _moneyAnswers = new int[3];
    int _moneyCorrect;

    [CreateProperty] public string MoneyQuestion => _moneyQuestion;
    [CreateProperty] public string MoneyAnswer1 => $"R$ {_moneyAnswers[0]},00";
    [CreateProperty] public string MoneyAnswer2 => $"R$ {_moneyAnswers[1]},00";
    [CreateProperty] public string MoneyAnswer3 => $"R$ {_moneyAnswers[2]},00";

    // Posto: Balança (código do produto)
    string _codeTarget = "";
    string _code = "";

    [CreateProperty] public string CodeSlot1 => Slot(_code, 0);
    [CreateProperty] public string CodeSlot2 => Slot(_code, 1);
    [CreateProperty] public string CodeSlot3 => Slot(_code, 2);
    [CreateProperty] public string CodeSlot4 => Slot(_code, 3);

    // Posto: Etiquetas (nome do produto)
    string _wordTarget = "";
    string _wordHint = "";
    string _word = "";
    char[] _tiles = new char[4];
    int _tileMask;

    [CreateProperty] public string WordsHint => $"Dica: {_wordHint}";
    [CreateProperty] public string WordSlot1 => Slot(_word, 0);
    [CreateProperty] public string WordSlot2 => Slot(_word, 1);
    [CreateProperty] public string WordSlot3 => Slot(_word, 2);
    [CreateProperty] public string WordSlot4 => Slot(_word, 3);
    [CreateProperty] public string Tile1 => _tiles[0].ToString();
    [CreateProperty] public string Tile2 => _tiles[1].ToString();
    [CreateProperty] public string Tile3 => _tiles[2].ToString();
    [CreateProperty] public string Tile4 => _tiles[3].ToString();

    /// <summary>Bit i ligado = letra i já usada (a View desativa o botão).</summary>
    public int TileUsedMask { get => _tileMask; private set => Set(ref _tileMask, value, nameof(TileUsedMask)); }

    // Resultado do turno
    string _resultTitle = "Turno concluído!";
    string _resultStars = "";
    string _resultCoins = "";
    string _resultCustomers = "";

    [CreateProperty] public string ResultTitle { get => _resultTitle; private set => Set(ref _resultTitle, value, nameof(ResultTitle)); }
    [CreateProperty] public string ResultStars { get => _resultStars; private set => Set(ref _resultStars, value, nameof(ResultStars)); }
    [CreateProperty] public string ResultCoins { get => _resultCoins; private set => Set(ref _resultCoins, value, nameof(ResultCoins)); }
    [CreateProperty] public string ResultCustomers { get => _resultCustomers; private set => Set(ref _resultCustomers, value, nameof(ResultCustomers)); }

    // =====================================================================
    // Construção e persistência
    // =====================================================================
    public AppViewModel(FlowVersion flow)
    {
        Flow = flow;
        ShopItems = new[]
        {
            new ShopItem("Calculadora", "Ajuda a conferir as contas no caixa.", 30),
            new ShopItem("Avental novo", "Um uniforme limpinho para o seu turno.", 50),
            new ShopItem("Crachá dourado", "Mostra que você é funcionário destaque.", 80),
        };
        Load();
        _selectedLevel = Mathf.Min(_levelsCompleted + 1, TotalLevels);
    }

    void Load()
    {
        _playerName = PlayerPrefs.GetString("playerName", _playerName);
        _coins = PlayerPrefs.GetInt("coins", _coins);
        _customers = PlayerPrefs.GetInt("customers", 0);
        _levelsCompleted = Mathf.Clamp(PlayerPrefs.GetInt("levelsCompleted", 0), 0, TotalLevels);
        _soundOn = PlayerPrefs.GetInt("soundOn", 1) == 1;
        _musicOn = PlayerPrefs.GetInt("musicOn", 1) == 1;
        _textScale = (TextScale)Mathf.Clamp(PlayerPrefs.GetInt("textScale", 0), 0, 2);
    }

    public void Save()
    {
        PlayerPrefs.SetString("playerName", _playerName);
        PlayerPrefs.SetInt("coins", _coins);
        PlayerPrefs.SetInt("customers", _customers);
        PlayerPrefs.SetInt("levelsCompleted", _levelsCompleted);
        PlayerPrefs.SetInt("soundOn", _soundOn ? 1 : 0);
        PlayerPrefs.SetInt("musicOn", _musicOn ? 1 : 0);
        PlayerPrefs.SetInt("textScale", (int)_textScale);
        PlayerPrefs.Save();
    }

    // =====================================================================
    // Comandos de navegação
    // =====================================================================
    public void FinishLoading() => Go(ScreenId.Menu);

    public void Go(ScreenId screen)
    {
        _matchId++; // cancela ações agendadas do turno anterior
        Overlay = OverlayId.None;
        if (screen == ScreenId.Market) MarketMessage = "";
        Screen = screen;
    }

    /// <summary>Botão TRABALHAR: versão A abre os dias de trabalho, versão B começa direto.</summary>
    public void OpenPlay()
    {
        if (Flow == FlowVersion.A)
        {
            Go(ScreenId.Levels);
        }
        else
        {
            SelectedLevel = Mathf.Min(_levelsCompleted + 1, TotalLevels);
            StartMatch();
        }
    }

    public void SelectLevel(int number)
    {
        if (number < 1 || number > TotalLevels || number > _levelsCompleted + 1) return;
        SelectedLevel = number;
        Go(ScreenId.LevelInfo);
    }

    public void NextLevel()
    {
        if (_selectedLevel >= TotalLevels) return;
        SelectedLevel = _selectedLevel + 1;
        Go(ScreenId.LevelInfo);
    }

    public void PlayAgain()
    {
        if (Flow == FlowVersion.B) SelectedLevel = Mathf.Min(_levelsCompleted + 1, TotalLevels);
        StartMatch();
    }

    // ---------- Pausa e saída ----------
    public void PauseGame() { if (Screen == ScreenId.Game) Overlay = OverlayId.Pause; }
    public void ResumeGame() => Overlay = OverlayId.None;

    public void AskQuit(bool fromPause)
    {
        _quitFromPause = fromPause;
        Overlay = OverlayId.Quit;
    }

    public void CancelQuit() => Overlay = _quitFromPause ? OverlayId.Pause : OverlayId.None;

    public void ConfirmQuit()
    {
        Save();
        QuitRequested?.Invoke();
    }

    // ---------- Configurações ----------
    public void ToggleSound() => SoundOn = !_soundOn;
    public void ToggleMusic() => MusicOn = !_musicOn;
    public void SetTextScale(TextScale scale) => TextScale = scale;

    // ---------- Loja do funcionário ----------
    public void Buy(int index)
    {
        if (index < 0 || index >= ShopItems.Length) return;
        var item = ShopItems[index];

        if (_coins < item.Price)
        {
            MarketMessage = "Você ainda não tem moedas suficientes.";
            return;
        }

        Coins = _coins - item.Price;
        MarketMessage = $"Compra feita: {item.Name}!";
        Save();
        // Aqui você pode registrar o item no inventário do funcionário.
    }

    // =====================================================================
    // Turno
    // =====================================================================
    public void StartMatch()
    {
        _mistakes = 0;
        _locked = false;
        _step = 0;
        ClearFeedback();
        SetupMiniGame();
        Go(ScreenId.Game);
    }

    void SetupMiniGame()
    {
        CurrentMiniGame = (MiniGameId)_step;
        _locked = false;

        switch (CurrentMiniGame)
        {
            case MiniGameId.Money:
            {
                _station = "Caixa";
                int correct;

                if (Random.value < 0.5f)
                {
                    // Total da compra
                    int a = Random.Range(2, 10);
                    int b = Random.Range(2, 10);
                    correct = a + b;
                    _moneyQuestion = $"Dois produtos: R$ {a},00 e R$ {b},00. Qual é o total da compra?";
                    _instructionExtra = "Toque no total certo.";
                }
                else
                {
                    // Troco
                    int paid = Random.value < 0.5f ? 10 : 20;
                    int price = Random.Range(3, paid - 1);
                    correct = paid - price;
                    _moneyQuestion = $"O cliente pagou R$ {paid},00 e a compra deu R$ {price},00. Qual é o troco?";
                    _instructionExtra = "Toque no troco certo.";
                }

                var options = new List<int> { correct - 1, correct, correct + 1 };
                Shuffle(options);
                for (int i = 0; i < 3; i++)
                {
                    _moneyAnswers[i] = options[i];
                    if (options[i] == correct) _moneyCorrect = i;
                }

                Notify(nameof(MoneyQuestion));
                Notify(nameof(MoneyAnswer1));
                Notify(nameof(MoneyAnswer2));
                Notify(nameof(MoneyAnswer3));
                break;
            }

            case MiniGameId.Code:
            {
                _station = "Balança";
                _codeTarget = Random.Range(1000, 10000).ToString();
                _code = "";
                string product = Products[Random.Range(0, Products.Length)];
                _instructionExtra = $"Pese {product}: digite o código {string.Join(" ", _codeTarget.ToCharArray())} e toque em OK.";
                NotifyCode();
                break;
            }

            case MiniGameId.Words:
            {
                _station = "Etiquetas";
                var entry = WordBank[Random.Range(0, WordBank.Length)];
                _wordTarget = entry.word;
                _wordHint = entry.hint;
                _word = "";
                var letters = new List<char>(_wordTarget.ToCharArray());
                do { Shuffle(letters); } while (new string(letters.ToArray()) == _wordTarget);
                _tiles = letters.ToArray();
                _instructionExtra = "Toque nas letras para montar o nome do produto.";
                Notify(nameof(WordsHint));
                Notify(nameof(Tile1));
                Notify(nameof(Tile2));
                Notify(nameof(Tile3));
                Notify(nameof(Tile4));
                NotifyWord();
                TileUsedMask = 0;
                break;
            }
        }

        Notify(nameof(GameInstruction));
    }

    // ---------- Posto: Caixa ----------
    public void AnswerMoney(int index)
    {
        if (_locked || CurrentMiniGame != MiniGameId.Money) return;
        if (index == _moneyCorrect) Correct();
        else Wrong("Quase! Confira a conta de novo.");
    }

    // ---------- Posto: Balança ----------
    public void PressKey(int digit)
    {
        if (_locked || CurrentMiniGame != MiniGameId.Code || _code.Length >= 4) return;
        _code += digit.ToString();
        NotifyCode();
    }

    public void EraseCode()
    {
        if (_locked || CurrentMiniGame != MiniGameId.Code || _code.Length == 0) return;
        _code = _code.Substring(0, _code.Length - 1);
        NotifyCode();
    }

    public void ConfirmCode()
    {
        if (_locked || CurrentMiniGame != MiniGameId.Code) return;

        if (_code.Length < 4)
        {
            ShowFeedback(FeedbackKind.Error, "Faltam números. Digite os 4 números.", 2000);
        }
        else if (_code == _codeTarget)
        {
            Correct();
        }
        else
        {
            Wrong("Código diferente. Confira na balança e tente de novo.");
            _code = "";
            NotifyCode();
        }
    }

    // ---------- Posto: Etiquetas ----------
    public void PressTile(int index)
    {
        if (_locked || CurrentMiniGame != MiniGameId.Words) return;
        if (index < 0 || index >= _tiles.Length || (_tileMask & (1 << index)) != 0) return;

        _word += _tiles[index];
        TileUsedMask = _tileMask | (1 << index);
        NotifyWord();

        if (_word.Length < _wordTarget.Length) return;

        if (_word == _wordTarget)
        {
            Correct();
        }
        else
        {
            Wrong("Essa etiqueta não está certa. Tente de novo.");
            EraseWord();
        }
    }

    public void EraseWord()
    {
        if (_locked || CurrentMiniGame != MiniGameId.Words) return;
        _word = "";
        TileUsedMask = 0;
        NotifyWord();
    }

    // ---------- Acerto, erro e avanço ----------
    void Correct()
    {
        _locked = true;
        ShowFeedback(FeedbackKind.Ok, "Cliente satisfeito!", 0);

        int id = _matchId;
        Delay(1200, () =>
        {
            if (id != _matchId) return; // turno cancelado ou reiniciado
            ClearFeedback();
            _step++;
            if (_step >= MiniGamesPerMatch) FinishMatch();
            else SetupMiniGame();
        });
    }

    // Erros não tiram nada do jogador: só contam para a avaliação (estrelas).
    void Wrong(string message)
    {
        _mistakes++;
        ShowFeedback(FeedbackKind.Error, message, 1800);
    }

    void ShowFeedback(FeedbackKind kind, string message, long clearAfterMs)
    {
        int token = ++_feedbackToken;
        Feedback = message;
        FeedbackKind = kind;

        if (clearAfterMs > 0)
        {
            Delay(clearAfterMs, () =>
            {
                if (token == _feedbackToken) ClearFeedback();
            });
        }
    }

    void ClearFeedback()
    {
        _feedbackToken++;
        FeedbackKind = FeedbackKind.None;
        Feedback = "";
    }

    void FinishMatch()
    {
        int stars = _mistakes == 0 ? 3 : (_mistakes <= 2 ? 2 : 1);
        int reward = stars * 10;

        Coins = _coins + reward;
        Customers = _customers + MiniGamesPerMatch;
        LevelsCompleted = Mathf.Max(_levelsCompleted, _selectedLevel);

        ResultTitle = "Turno concluído!";
        ResultStars = $"Avaliação dos clientes: {stars} de 3 estrelas";
        ResultCoins = $"Você recebeu {reward} moedas pelo turno";
        ResultCustomers = $"Clientes atendidos: {MiniGamesPerMatch}";

        Save();
        Go(ScreenId.Result);
    }

    // =====================================================================
    // Auxiliares
    // =====================================================================
    void NotifyCode()
    {
        Notify(nameof(CodeSlot1));
        Notify(nameof(CodeSlot2));
        Notify(nameof(CodeSlot3));
        Notify(nameof(CodeSlot4));
    }

    void NotifyWord()
    {
        Notify(nameof(WordSlot1));
        Notify(nameof(WordSlot2));
        Notify(nameof(WordSlot3));
        Notify(nameof(WordSlot4));
    }

    static string Slot(string text, int index) => index < text.Length ? text[index].ToString() : "";

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
