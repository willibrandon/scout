namespace Scout;

/// <summary>
/// Lazily compiles shared immutable forward and reverse NFAs and creates independent DFA runners.
/// </summary>
/// <param name="nfa">The authoritative anchored forward NFA.</param>
/// <param name="root">The parsed regex root.</param>
/// <param name="options">The root compilation options.</param>
/// <param name="dfaSizeLimit">The maximum estimated storage for each lazy DFA.</param>
/// <param name="constructionBudget">The optional budget that already covers a compiled forward NFA.</param>
/// <param name="forwardNfaIsUnanchored">Whether <paramref name="nfa" /> is already unanchored.</param>
internal sealed class RegexUnanchoredLazyDfaFactory(
    RegexNfa nfa,
    RegexSyntaxNode root,
    RegexCompileOptions options,
    ulong dfaSizeLimit,
    RegexNfaConstructionBudget? constructionBudget = null,
    bool forwardNfaIsUnanchored = false)
{
    private const int ReverseAvailabilityUnknown = 0;
    private const int ReverseAvailable = 1;
    private const int ReverseUnavailable = -1;

    private RegexNfa? _nfa = forwardNfaIsUnanchored ? null : nfa;
    private RegexSyntaxNode? _root = root;
    private RegexCompileOptions _options = options;
    private readonly ulong _dfaSizeLimit = dfaSizeLimit;
    private readonly RegexNfaConstructionBudget _constructionBudget =
        constructionBudget ?? new RegexNfaConstructionBudget(dfaSizeLimit);
    private readonly bool _constructionEligible = forwardNfaIsUnanchored ||
        RegexNfaCompiler.EstimateUnanchoredConstruction(root, options).ForwardFits(dfaSizeLimit);
    private readonly object _forwardInitializationLock = new();
    private readonly object _reverseInitializationLock = new();
    private RegexNfa? _forwardNfa = forwardNfaIsUnanchored ? nfa : null;
    private RegexNfa? _reverseNfa;
    private int _forwardInitializationCount;
    private int _forwardInitialized = forwardNfaIsUnanchored ? 1 : 0;
    private int _reverseInitializationCount;
    private int _reverseInitialized;
    private int _reverseAvailability = ReverseAvailabilityUnknown;

    /// <summary>
    /// Gets the number of times forward-NFA initialization entered its critical section.
    /// </summary>
    internal int InitializationCount => System.Threading.Volatile.Read(ref _forwardInitializationCount);

    /// <summary>
    /// Gets the number of times reverse-NFA initialization entered its critical section.
    /// </summary>
    internal int ReverseInitializationCount => System.Threading.Volatile.Read(ref _reverseInitializationCount);

    /// <summary>
    /// Gets a value indicating whether reverse-DFA construction has permanently failed.
    /// </summary>
    internal bool IsReverseUnavailable =>
        System.Threading.Volatile.Read(ref _reverseAvailability) == ReverseUnavailable;

    /// <summary>
    /// Creates an independent mutable runner from the shared immutable forward NFA.
    /// </summary>
    /// <returns>The runner, or <see langword="null" /> when the pattern is ineligible.</returns>
    internal RegexUnanchoredLazyDfa? Create()
    {
        EnsureForwardInitialized();
        RegexNfa? forwardNfa = _forwardNfa;
        if (forwardNfa is null ||
            !RegexLazyDfa.TryCreate(
                forwardNfa,
                _dfaSizeLimit,
                leftmostPrune: true,
                out RegexLazyDfa? forwardDfa))
        {
            return null;
        }

        return new RegexUnanchoredLazyDfa(
            forwardDfa!,
            reverse: null,
            reverseFactory: this);
    }

    /// <summary>
    /// Creates an independent mutable reverse DFA, compiling its shared NFA on first demand.
    /// </summary>
    /// <returns>The reverse DFA, or <see langword="null" /> when reverse execution is ineligible.</returns>
    internal RegexLazyDfa? CreateReverseDfa()
    {
        if (IsReverseUnavailable)
        {
            return null;
        }

        EnsureReverseInitialized();
        RegexNfa? reverseNfa = _reverseNfa;
        if (reverseNfa is null)
        {
            System.Threading.Volatile.Write(ref _reverseAvailability, ReverseUnavailable);
            return null;
        }

        if (RegexLazyDfa.TryCreate(
                reverseNfa,
                _dfaSizeLimit,
                leftmostPrune: true,
                out RegexLazyDfa? reverseDfa))
        {
            System.Threading.Volatile.Write(ref _reverseAvailability, ReverseAvailable);
            return reverseDfa;
        }

        System.Threading.Volatile.Write(ref _reverseAvailability, ReverseUnavailable);
        return null;
    }

    private void EnsureForwardInitialized()
    {
        if (System.Threading.Volatile.Read(ref _forwardInitialized) != 0)
        {
            return;
        }

        lock (_forwardInitializationLock)
        {
            if (_forwardInitialized != 0)
            {
                return;
            }

            _forwardInitializationCount++;
            try
            {
                if (_constructionEligible &&
                    RegexUnanchoredLazyDfa.TryCompileForwardNfa(
                        _nfa!,
                        _root!,
                        _options,
                        _constructionBudget,
                        out RegexNfa? forwardNfa))
                {
                    _forwardNfa = forwardNfa;
                }
            }
            finally
            {
                _nfa = null;
                if (_forwardNfa is null)
                {
                    ClearReverseInputs();
                }

                System.Threading.Volatile.Write(ref _forwardInitialized, 1);
            }
        }
    }

    private void EnsureReverseInitialized()
    {
        if (System.Threading.Volatile.Read(ref _reverseInitialized) != 0)
        {
            return;
        }

        lock (_reverseInitializationLock)
        {
            if (_reverseInitialized != 0)
            {
                return;
            }

            _reverseInitializationCount++;
            try
            {
                if (_root is not null &&
                    RegexUnanchoredLazyDfa.TryCompileReverseNfa(
                        _root,
                        _options,
                        _constructionBudget,
                        out RegexNfa? reverseNfa) &&
                    _forwardNfa is not null &&
                    _constructionBudget.CanRetain(_forwardNfa, reverseNfa!))
                {
                    _reverseNfa = reverseNfa;
                }
            }
            finally
            {
                if (_reverseNfa is null)
                {
                    System.Threading.Volatile.Write(ref _reverseAvailability, ReverseUnavailable);
                }

                ClearReverseInputs();
                System.Threading.Volatile.Write(ref _reverseInitialized, 1);
            }
        }
    }

    private void ClearReverseInputs()
    {
        _root = null;
        _options = default;
    }
}
