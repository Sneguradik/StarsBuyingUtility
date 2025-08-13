using Buyer.Models;
using TdLib;
using TdLib.Bindings;
using TdLogLevel = TdLib.Bindings.TdLogLevel;

namespace Buyer.Services;

public interface ITelegramGiftBuyer
{
    Task InitAsync(CancellationToken cancellationToken = default);
    
    Task<IEnumerable<Gift>> GetAvailableGiftsAsync(CancellationToken cancellationToken = default);

    Task<GiftTransaction?> BuyGiftAsync(Gift gift, long recipientId, RecipientType type,
        CancellationToken cancellationToken = default);
};

public class TelegramGiftBuyer(ILogger<TelegramGiftBuyer> logger) : ITelegramGiftBuyer, IDisposable
{
    private readonly TdClient _client = new ();
    private bool _authNeeded;

    public async Task InitAsync(CancellationToken cancellationToken = default)
    {
        _client.Bindings.SetLogVerbosityLevel(TdLogLevel.Fatal);
        
        _client.UpdateReceived += async (_, update) => { await ProcessUpdates(update); };

        _readyToAuthenticate.Wait(cancellationToken);
        
        if (_authNeeded)
        {
            await HandleAuthentication();
        }
    }

    public async Task<IEnumerable<Gift>> GetAvailableGiftsAsync(CancellationToken cancellationToken = default)
    {
        var starGiftsResult = await _client.ExecuteAsync(new TdApi.GetAvailableGifts());
        if (starGiftsResult?.Gifts_ == null)
            return [];
        
        var chat = await _client.GetChatsAsync(limit:20);
        logger.LogInformation($"Found {chat.ChatIds.Length} chats");
        

        return starGiftsResult.Gifts_.Select(g => new Gift
        {
            Id = g.Id,
            Price= g.StarCount,
            Limited = g.TotalCount != 0,
            TotalSupply = g.TotalCount != 0 ? g.TotalCount : null,
            CurrentSupply = g.TotalCount != 0 ? g.RemainingCount : null,
        }).ToList();
    }

    public async Task<GiftTransaction?> BuyGiftAsync(Gift gift, long recipientId, RecipientType type, CancellationToken cancellationToken = default)
    {
        try
        {
            TdApi.MessageSender sender;

            if (type == RecipientType.Channel)
            {
                sender = new TdApi.MessageSender.MessageSenderChat()  { ChatId = recipientId };
            }
            else
                sender = new TdApi.MessageSender.MessageSenderUser() { UserId = recipientId };
            
            var exec = await _client.SendGiftAsync(
                gift.Id, 
                sender, 
                new TdApi.FormattedText(){Text = $"Gift for {recipientId}"}, 
                true, false);


            if (exec != null)
                return new GiftTransaction
                    { GiftId = gift.Id, RecipientId = recipientId, TransactionDate = DateTime.UtcNow, Price = (int)gift.Price };
            
            logger.LogInformation($"Gift {gift.Id} for {recipientId} failed");
            return null;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to buy gift {gift}", gift);
            logger.LogError(e, e.Message);
            return null;
        }
        
    }
    
    private async Task ProcessUpdates(TdApi.Update update)
    {
        // Since Tdlib was made to be used in GUI application we need to struggle a bit and catch required events to determine our state.
        // Below you can find example of simple authentication handling.
        // Please note that AuthorizationStateWaitOtherDeviceConfirmation is not implemented.

        switch (update)
        {
            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters }:
                // TdLib creates database in the current directory.
                // so create separate directory and switch to that dir.
                var filesLocation = Path.Combine(AppContext.BaseDirectory, "db");
                await _client.ExecuteAsync(new TdApi.SetTdlibParameters
                {
                    ApiId = ApiId,
                    ApiHash = ApiHash,
                    DeviceModel = "PC",
                    SystemLanguageCode = "en",
                    ApplicationVersion = ApplicationVersion,
                    DatabaseDirectory = filesLocation,
                    FilesDirectory = filesLocation,
                    // More parameters available!
                });
                break;

            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber }:
            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitCode }:
                _authNeeded = true;
                _readyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPassword }:
                _authNeeded = true;
                _passwordNeeded = true;
                _readyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateUser:
                _readyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateConnectionState { State: TdApi.ConnectionState.ConnectionStateReady }:
                // You may trigger additional event on connection state change
                break;

            default:
                // ReSharper disable once EmptyStatement
                ;
                // Add a breakpoint here to see other events
                break;
        }
    }
    
    private async Task HandleAuthentication()
    {
        Console.WriteLine("Authenticating with Telegram. Write phone number starting from +...");
        var phoneNumber = Console.ReadLine();
        await _client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
        {
            PhoneNumber = phoneNumber
        });
        
        Console.Write("Insert the login code: ");
        var code = Console.ReadLine();

        await _client.CheckAuthenticationEmailCodeAsync(new TdApi.EmailAddressAuthentication.EmailAddressAuthenticationCode()
        {
            Code = code,
        });

        if(!_passwordNeeded) { return; }
        
        Console.Write("Insert the password: ");
        var password = Console.ReadLine();

        await _client.ExecuteAsync(new TdApi.CheckAuthenticationPassword
        {
            Password = password
        });
    }
    

    private string ApplicationVersion { get; set; } = "1.0.0";
    private string ApiHash { get; set; } = "a665315d07ed7fa0774faaf5c04be3bf";
    private readonly ManualResetEventSlim _readyToAuthenticate = new ();
    private bool _passwordNeeded;
    private int ApiId { get; set; } = 24486347;

    public void Dispose()
    {
        _client.Dispose();
    }
}