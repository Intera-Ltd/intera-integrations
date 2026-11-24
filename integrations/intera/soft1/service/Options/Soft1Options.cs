namespace Intera.Soft1.Api.Options;

public sealed class Soft1Options
{
    public const string SectionName = "Soft1";

    /// <summary>
    /// The installation's web services endpoint, e.g. <c>https://mycompany.oncloud.gr/s1services</c>.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>A Soft1 web account (not a desktop user) with the Web Service Connector licence.</summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The application id the web account is registered for. Every request after sign-in repeats the one the
    /// session was opened with.
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// The company, branch, module and reference id to authenticate into. Empty means the only one the account
    /// may use.
    /// </summary>
    public string Company { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public string RefId { get; set; } = string.Empty;

    /// <summary>Rows per <c>getBrowserInfo</c>/<c>getBrowserData</c> page.</summary>
    public int PageSize { get; set; } = 500;

    /// <summary>Most rows one browser read may return.</summary>
    public int MaxRowsPerRead { get; set; } = 50_000;

    /// <summary>Most <c>getData</c> calls one query may spend.</summary>
    public int MaxRecordReadsPerQuery { get; set; } = 2_000;

    /// <summary>Most requests in flight to Soft1 at once, shared by every query this process serves.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    public int HttpTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// How many fiscal years a query reads when its filter names no date range.
    /// </summary>
    public int DefaultFiscalYears { get; set; } = 2;

    /// <summary>Most fiscal years one query may read.</summary>
    public int MaxFiscalYears { get; set; } = 5;

    /// <summary>
    /// Document behaviours (<c>TFPRMS</c>) that make a document an order; empty means 201.
    /// </summary>
    public int[] OrderBehaviours { get; set; } = [];

    /// <summary>What <c>getData</c> asks of a document when its item lines are read.</summary>
    public string LineLocateInfo { get; set; } =
        "ITELINES:MTRLINES,LINENUM,MTRL,MTRL_ITEM_CODE,MTRL_ITEM_NAME,QTY1,QTY1COV,QTY1CANC,MTRUNIT,PRICE,DISC1PRC,DISC1VAL," +
        "LINEVAL,NETLINEVAL,VATAMNT,WHOUSE,PRJC,DELIVDATE,COMMENTS";

    /// <summary>What <c>getData</c> asks of a document for its VAT analysis.</summary>
    public string VatLocateInfo { get; set; } = "VATANAL:LINENUM,VAT,VAT_VAT_PERCNT,SUBVAL,VATVAL";

    /// <summary>Per-read installation overrides keyed by read id.</summary>
    public Dictionary<string, Soft1ReadOverride> Reads { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What an installation may change about one read. Every property is optional.</summary>
public sealed class Soft1ReadOverride
{
    public Services.Soft1ReadMode? Mode { get; set; }

    public string? Object { get; set; }

    public string? List { get; set; }

    public string? Filters { get; set; }

    public string? DateFilter { get; set; }

    public string? SeriesFilter { get; set; }

    public string? KeyName { get; set; }

    public string? KeyValue { get; set; }

    public Dictionary<string, string> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
