using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI.Router.Adapters;

/// <summary>
/// DeepSeek R1 model adapter.
/// Priority 1 - default model for reasoning-heavy requests.
/// </summary>
public class DeepSeekAdapter : BaseModelAdapter
{
    private readonly ModelConfig _config;
    
    public override string ModelId => _config.ModelId;
    public override string DisplayName => "DeepSeek R1";
    public override int Priority => 1;
    public override bool IsEnabled => _config.Enabled && !string.IsNullOrEmpty(_config.ApiKey);
    public override int TimeoutMs => _config.TimeoutSeconds * 1000;
    
    protected override string Endpoint => _config.Endpoint;
    protected override string ApiKey => _config.ApiKey;
    protected override int MaxTokens => _config.MaxTokens;
    protected override double Temperature => 0.3;
    
    public DeepSeekAdapter(
        HttpClient httpClient,
        IOptions<CommerceAIOptions> options,
        ILogger<DeepSeekAdapter> logger) 
        : base(httpClient, logger)
    {
        _config = options.Value.Models.GetValueOrDefault("DeepSeek") ?? new ModelConfig
        {
            Enabled = true,
            ModelId = "deepseek/deepseek-r1",
            Endpoint = "https://openrouter.ai/api/v1/chat/completions",
            TimeoutSeconds = 30,
            MaxTokens = 1000
        };
    }
}

/// <summary>
/// GPT-OSS model adapter.
/// Priority 2 - fallback if DeepSeek fails.
/// </summary>
public class GptOssAdapter : BaseModelAdapter
{
    private readonly ModelConfig _config;
    
    public override string ModelId => _config.ModelId;
    public override string DisplayName => "GPT-OSS-120B";
    public override int Priority => 2;
    public override bool IsEnabled => _config.Enabled && !string.IsNullOrEmpty(_config.ApiKey);
    public override int TimeoutMs => _config.TimeoutSeconds * 1000;
    
    protected override string Endpoint => _config.Endpoint;
    protected override string ApiKey => _config.ApiKey;
    protected override int MaxTokens => _config.MaxTokens;
    protected override double Temperature => 0.3;
    
    public GptOssAdapter(
        HttpClient httpClient,
        IOptions<CommerceAIOptions> options,
        ILogger<GptOssAdapter> logger) 
        : base(httpClient, logger)
    {
        _config = options.Value.Models.GetValueOrDefault("GptOss") ?? new ModelConfig
        {
            Enabled = true,
            ModelId = "huggingfaceh4/zephyr-orpo-141b-a35b-v0.1",
            Endpoint = "https://openrouter.ai/api/v1/chat/completions",
            TimeoutSeconds = 25,
            MaxTokens = 1000
        };
    }
}

/// <summary>
/// Gemma 3 4B model adapter.
/// Priority 3 - emergency fallback (fast, small model).
/// </summary>
public class GemmaAdapter : BaseModelAdapter
{
    private readonly ModelConfig _config;
    
    public override string ModelId => _config.ModelId;
    public override string DisplayName => "Gemma 3 4B";
    public override int Priority => 3;
    public override bool IsEnabled => _config.Enabled && !string.IsNullOrEmpty(_config.ApiKey);
    public override int TimeoutMs => _config.TimeoutSeconds * 1000;
    
    protected override string Endpoint => _config.Endpoint;
    protected override string ApiKey => _config.ApiKey;
    protected override int MaxTokens => _config.MaxTokens;
    protected override double Temperature => 0.3;
    
    public GemmaAdapter(
        HttpClient httpClient,
        IOptions<CommerceAIOptions> options,
        ILogger<GemmaAdapter> logger) 
        : base(httpClient, logger)
    {
        _config = options.Value.Models.GetValueOrDefault("Gemma") ?? new ModelConfig
        {
            Enabled = true,
            ModelId = "google/gemma-3-4b-it",
            Endpoint = "https://openrouter.ai/api/v1/chat/completions",
            TimeoutSeconds = 15,
            MaxTokens = 800
        };
    }
}
