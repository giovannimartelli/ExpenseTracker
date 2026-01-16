using System.Globalization;
using ExpenseTracker.Services;
using ExpenseTracker.TelegramBot.TelegramBot.Utils;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExpenseTracker.TelegramBot.TelegramBot.Flows;

/// <summary>
/// Handles the flow for inserting a new expense.
/// Flow: Menu → Category → Subcategory → Tag → Description → Amount → Date → Save
/// </summary>
public class InsertExpenseFlowHandler(
    IServiceScopeFactory scopeFactory,
    IOptions<WebAppOptions> webAppOptions,
    Lazy<FlowController> flowController,
    ILogger<InsertExpenseFlowHandler> logger) : FlowHandler
{
    private readonly WebAppOptions _webAppOptions = webAppOptions.Value;

    private const string MenuCommandText = "💰 Inserisci spesa";

    private const string CallbackCategoryPrefix = "addexpenses_cat";
    private const string CallbackSubCategoryPrefix = "addexpenses_sub";
    private const string CallbackTagPrefix = "addexpenses_tag";
    private const string CallbackSkipTag = "addexpenses_skiptag";

    // Date selection buttons (ReplyKeyboard - text messages)
    private const string ButtonUseTodayDate = "📅 Usa data di oggi";
    private const string ButtonChooseDate = "📆 Scegli altra data";
    private const string ButtonBack = "◀️ Indietro";
    private const string ButtonMainMenu = "🏠 Menu principale";

    public override string GetMenuItemInfo() => MenuCommandText;
    public override bool CanHandleMenuCommand(string command) => command == MenuCommandText;

    public override async Task HandleMenuSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting insert expense flow for chat {ChatId}", chat.Id);

        var flowData = new InsertExpenseFlowData();
        state.SetFlowData(flowData);

        await ShowCategoriesAsync(botClient, chat, state, flowData, cancellationToken);
    }

    public override bool CanHandleCallback(string callbackName, string callbackData, ConversationState state)
    {
        var flowData = state.GetFlowData<InsertExpenseFlowData>();
        if (flowData == null) return false;

        return flowData.CurrentStep switch
        {
            InsertExpenseFlowData.StepSelectCategory => callbackName == CallbackCategoryPrefix,
            InsertExpenseFlowData.StepSelectSubCategory => callbackName == CallbackSubCategoryPrefix,
            InsertExpenseFlowData.StepSelectTag => callbackName is CallbackTagPrefix or CallbackSkipTag,
            _ => false
        };
    }

    public override async Task HandleCallbackAsync(
        ITelegramBotClient botClient,
        string callbackName,
        string callbackData,
        CallbackQuery callbackQuery,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = callbackQuery.Message!.Chat;
        var flowData = state.GetFlowData<InsertExpenseFlowData>()!;

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

        if (callbackName == CallbackCategoryPrefix)
        {
            var categoryId = int.Parse(callbackData);

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var category = await categoryService.GetCategoryByIdAsync(categoryId);

            if (category == null)
            {
                await botClient.SendMessage(chat.Id, "❌ Category not found.", cancellationToken: cancellationToken);
                return;
            }

            flowData.CategoryId = categoryId;
            flowData.CategoryName = category.Name;
            flowData.CurrentStep = InsertExpenseFlowData.StepSelectSubCategory;

            logger.LogInformation("Category selected: {CategoryId} - {CategoryName}", categoryId, category.Name);
            await ShowSubCategoriesAsync(botClient, chat, state, flowData, cancellationToken);
        }
        else if (callbackName == CallbackSubCategoryPrefix)
        {
            var subCategoryId = int.Parse(callbackData);

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var subCategory = await categoryService.GetSubCategoryByIdAsync(subCategoryId);

            if (subCategory == null)
            {
                await botClient.SendMessage(chat.Id, "❌ Subcategory not found.", cancellationToken: cancellationToken);
                return;
            }

            flowData.SubCategoryId = subCategoryId;
            flowData.SubCategoryName = subCategory.Name;

            logger.LogInformation("SubCategory selected: {SubCategoryId} - {SubCategoryName}", subCategoryId, subCategory.Name);

            // Check if subcategory has tags
            var tags = await categoryService.GetTagsBySubCategoryIdAsync(subCategoryId);
            if (tags.Count > 0)
            {
                flowData.CurrentStep = InsertExpenseFlowData.StepSelectTag;
                await ShowTagsAsync(botClient, chat, state, flowData, tags, cancellationToken);
            }
            else
            {
                flowData.CurrentStep = InsertExpenseFlowData.StepAddDescription;
                await AskForDescriptionAsync(botClient, chat, state, flowData, cancellationToken);
            }
        }
        else if (callbackName == CallbackTagPrefix)
        {
            var tagId = int.Parse(callbackData);

            using var scope = scopeFactory.CreateScope();
            var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
            var tag = await categoryService.GetTagByIdAsync(tagId);

            if (tag == null)
            {
                await botClient.SendMessage(chat.Id, "❌ Tag not found.", cancellationToken: cancellationToken);
                return;
            }

            flowData.TagId = tagId;
            flowData.TagName = tag.Name;
            flowData.CurrentStep = InsertExpenseFlowData.StepAddDescription;

            logger.LogInformation("Tag selected: {TagId} - {TagName}", tagId, tag.Name);
            await AskForDescriptionAsync(botClient, chat, state, flowData, cancellationToken);
        }
        else if (callbackName == CallbackSkipTag)
        {
            flowData.TagId = null;
            flowData.TagName = null;
            flowData.CurrentStep = InsertExpenseFlowData.StepAddDescription;

            logger.LogInformation("Tag skipped");
            await AskForDescriptionAsync(botClient, chat, state, flowData, cancellationToken);
        }
    }

    public override bool CanHandleTextInput(ConversationState state)
    {
        var flowData = state.GetFlowData<InsertExpenseFlowData>();
        if (flowData == null) return false;

        return flowData.CurrentStep is InsertExpenseFlowData.StepAddDescription
            or InsertExpenseFlowData.StepInsertAmount
            or InsertExpenseFlowData.StepSelectDate;
    }

    public override async Task HandleTextInputAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var text = message.Text!;
        var flowData = state.GetFlowData<InsertExpenseFlowData>()!;

        if (flowData.CurrentStep == InsertExpenseFlowData.StepAddDescription)
        {
            flowData.Description = text;
            flowData.CurrentStep = InsertExpenseFlowData.StepInsertAmount;

            logger.LogInformation("Description entered: {Description}", text);
            await AskForAmountAsync(botClient, chat, state, flowData, cancellationToken);
        }
        else if (flowData.CurrentStep == InsertExpenseFlowData.StepInsertAmount)
        {
            await HandleAmountInputAsync(botClient, chat, text, state, flowData, cancellationToken);
        }
        else if (flowData.CurrentStep == InsertExpenseFlowData.StepSelectDate)
        {
            await HandleDateSelectionAsync(botClient, chat, text, state, flowData, cancellationToken);
        }
    }

    private async Task HandleDateSelectionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string text,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        if (text == ButtonUseTodayDate)
        {
            flowData.SelectedDate = DateOnly.FromDateTime(DateTime.UtcNow);
            logger.LogInformation("Using today's date: {Date}", flowData.SelectedDate);
            await SaveExpenseAsync(botClient, chat, state, flowData, cancellationToken);
        }
        else if (text == ButtonBack)
        {
            flowData.Amount = null;
            flowData.CurrentStep = InsertExpenseFlowData.StepInsertAmount;
            logger.LogInformation("Going back to amount input");
            await AskForAmountAsync(botClient, chat, state, flowData, cancellationToken);
        }
        else if (text == ButtonMainMenu)
        {
            state.Reset();
            logger.LogInformation("Returning to main menu");
            await ShowMainMenuMessageAsync(botClient, chat, cancellationToken);
        }
    }

    public override bool CanHandleBack(ConversationState state)
    {
        var flowData = state.GetFlowData<InsertExpenseFlowData>();
        if (flowData == null) return false;

        return flowData.CurrentStep is InsertExpenseFlowData.StepSelectSubCategory
            or InsertExpenseFlowData.StepSelectTag
            or InsertExpenseFlowData.StepAddDescription
            or InsertExpenseFlowData.StepInsertAmount;
    }

    public override async Task<bool> HandleBackAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var flowData = state.GetFlowData<InsertExpenseFlowData>()!;
        logger.LogInformation("Handling back from step {Step}", flowData.CurrentStep);

        switch (flowData.CurrentStep)
        {
            case InsertExpenseFlowData.StepSelectSubCategory:
                flowData.CategoryId = null;
                flowData.CategoryName = null;
                flowData.CurrentStep = InsertExpenseFlowData.StepSelectCategory;
                await ShowCategoriesAsync(botClient, chat, state, flowData, cancellationToken);
                return true;

            case InsertExpenseFlowData.StepSelectTag:
                flowData.SubCategoryId = null;
                flowData.SubCategoryName = null;
                flowData.CurrentStep = InsertExpenseFlowData.StepSelectSubCategory;
                await ShowSubCategoriesAsync(botClient, chat, state, flowData, cancellationToken);
                return true;

            case InsertExpenseFlowData.StepAddDescription:
                flowData.TagId = null;
                flowData.TagName = null;
                using (var scope = scopeFactory.CreateScope())
                {
                    var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
                    var tags = await categoryService.GetTagsBySubCategoryIdAsync(flowData.SubCategoryId!.Value);
                    if (tags.Count > 0)
                    {
                        flowData.CurrentStep = InsertExpenseFlowData.StepSelectTag;
                        await ShowTagsAsync(botClient, chat, state, flowData, tags, cancellationToken);
                        return true;
                    }
                }

                flowData.SubCategoryId = null;
                flowData.SubCategoryName = null;
                flowData.CurrentStep = InsertExpenseFlowData.StepSelectSubCategory;
                await ShowSubCategoriesAsync(botClient, chat, state, flowData, cancellationToken);
                return true;

            case InsertExpenseFlowData.StepInsertAmount:
                flowData.Description = null;
                flowData.CurrentStep = InsertExpenseFlowData.StepAddDescription;
                await AskForDescriptionAsync(botClient, chat, state, flowData, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    // ========== PRIVATE HELPER METHODS ==========

    private async Task ShowCategoriesAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
        var categories = await categoryService.GetAllCategoriesAsync();

        var buttons = categories
            .Select(c => new[] { Utils.Utils.ButtonWithCallbackdata(c.Name, CallbackCategoryPrefix, c.Id) })
            .ToList();

        buttons.Add([Utils.Utils.MainMenu]);

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = "📁 *Select a category:*";

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowSubCategoriesAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<CategoryService>();
        var subCategories = await categoryService.GetSubCategoriesByCategoryIdAsync(flowData.CategoryId!.Value);

        var buttons = subCategories
            .Select(c => new[] { Utils.Utils.ButtonWithCallbackdata(c.Name, CallbackSubCategoryPrefix, c.Id) })
            .ToList();

        buttons.Add([Utils.Utils.Back]);

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = $"📁 *{flowData.CategoryName}*\n\n📂 Select a subcategory:";

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task ShowTagsAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        List<Domain.Entities.Tag> tags,
        CancellationToken cancellationToken)
    {
        var buttons = tags
            .Select(t => new[] { Utils.Utils.ButtonWithCallbackdata($"🏷️ {t.Name}", CallbackTagPrefix, t.Id) })
            .ToList();

        buttons.Add([Utils.Utils.ButtonWithCallbackdata("⏭️ Salta", CallbackSkipTag, "skip")]);
        buttons.Add([Utils.Utils.Back]);

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = $"📁 *{flowData.CategoryName}* > *{flowData.SubCategoryName}*\n\n🏷️ Seleziona un tag:";

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskForDescriptionAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var text = $"📁 *{flowData.CategoryName}* > *{flowData.SubCategoryName}*\n\n" +
                   "📝 Enter a description for the expense:";

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task AskForAmountAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup([
            [Utils.Utils.Back],
            [Utils.Utils.MainMenu]
        ]);

        var text = $"📁 *{flowData.CategoryName}* > *{flowData.SubCategoryName}*\n" +
                   $"📝 {flowData.Description}\n\n" +
                   "💰 Enter the amount (e.g., 12.50):";

        var msg = await botClient.TryEditOrSendFlowMessageAsync(
            chat.Id,
            state,
            text,
            ParseMode.Markdown,
            keyboard,
            cancellationToken);

        state.LastBotMessageId = msg.MessageId;
    }

    private async Task HandleAmountInputAsync(
        ITelegramBotClient botClient,
        Chat chat,
        string amountText,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        var normalizedAmount = amountText.Replace(",", ".");

        if (!decimal.TryParse(normalizedAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            var message = await botClient.SendMessage(
                chatId: chat.Id,
                text: "❌ Invalid amount. Enter a positive number (e.g., 12.50):",
                cancellationToken: cancellationToken);
            state.LastBotMessageId = message.Id;
            return;
        }

        flowData.Amount = amount;
        flowData.CurrentStep = InsertExpenseFlowData.StepSelectDate;

        logger.LogInformation("Amount entered: {Amount}", amount);
        await AskForDateAsync(botClient, chat, state, flowData, cancellationToken);
    }

    private async Task AskForDateAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(ButtonUseTodayDate) },
            new[] { KeyboardButton.WithWebApp(ButtonChooseDate, new WebAppInfo { Url = _webAppOptions.DatePickerUrl }) },
            new[] { new KeyboardButton(ButtonBack), new KeyboardButton(ButtonMainMenu) }
        })
        {
            ResizeKeyboard = true
        };

        var tagInfo = flowData.TagName != null ? $"\n🏷️ {flowData.TagName}" : "";
        var text = $"📁 *{flowData.CategoryName}* > *{flowData.SubCategoryName}*{tagInfo}\n" +
                   $"📝 {flowData.Description}\n" +
                   $"💰 €{flowData.Amount:F2}\n\n" +
                   "📆 *Seleziona la data della spesa:*";

        var msg = await botClient.SendMessage(
            chatId: chat.Id,
            text: text,
            parseMode: ParseMode.Markdown,
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);

        state.TrackFlowMessage(msg.MessageId);
        state.LastBotMessageId = msg.MessageId;
    }

    public override bool CanHandleWebAppData(ConversationState state)
    {
        var flowData = state.GetFlowData<InsertExpenseFlowData>();
        return flowData?.CurrentStep == InsertExpenseFlowData.StepSelectDate;
    }

    public override async Task HandleWebAppDataAsync(
        ITelegramBotClient botClient,
        Message message,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        var chat = message.Chat;
        var dateString = message.WebAppData!.Data;
        var flowData = state.GetFlowData<InsertExpenseFlowData>()!;

        if (!DateOnly.TryParse(dateString, out var selectedDate))
        {
            logger.LogWarning("Invalid date received from WebApp: {DateString}", dateString);
            await botClient.SendFlowMessageAsync(
                chatId: chat.Id,
                state,
                text: "❌ Data non valida ricevuta. Riprova.",
                cancellationToken: cancellationToken);
            return;
        }

        flowData.SelectedDate = selectedDate;
        logger.LogInformation("Date selected from WebApp: {Date}", selectedDate);
        await SaveExpenseAsync(botClient, chat, state, flowData, cancellationToken);
    }

    private async Task SaveExpenseAsync(
        ITelegramBotClient botClient,
        Chat chat,
        ConversationState state,
        InsertExpenseFlowData flowData,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var expenseService = scope.ServiceProvider.GetRequiredService<ExpenseService>();

            await expenseService.CreateExpenseAsync(
                subCategoryId: flowData.SubCategoryId!.Value,
                amount: flowData.Amount!.Value,
                description: flowData.Description ?? throw new InvalidOperationException("Description cannot be null"),
                notes: null,
                performedBy: chat.Username ?? chat.Id.ToString(),
                tagId: flowData.TagId,
                flowData.SelectedDate!.Value);

            logger.LogInformation("Expense created: {Amount} - {Description} - Tag: {TagId} - Date: {Date}",
                flowData.Amount, flowData.Description, flowData.TagId, flowData.SelectedDate);

            var tagInfo = flowData.TagName != null ? $"\n🏷️ {flowData.TagName}" : "";
            var confirmationText = $"✅ *Spesa registrata!*\n\n" +
                                   $"📁 {flowData.CategoryName} > {flowData.SubCategoryName}{tagInfo}\n" +
                                   $"📝 {flowData.Description}\n" +
                                   $"💰 €{flowData.Amount:F2}\n" +
                                   $"📆 {flowData.SelectedDate:dd/MM/yyyy}";

            await botClient.SendFlowMessageAsync(
                chatId: chat.Id,
                state,
                text: confirmationText,
                cancellationToken: cancellationToken);
            await ShowMainMenuMessageAsFlowMessageAsync(botClient, state, chat, cancellationToken);

            state.Reset();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating expense");
            await botClient.SendMessage(
                chatId: chat.Id,
                text: "❌ Si è verificato un errore durante il salvataggio. Riprova.",
                replyMarkup: flowController.Value.MainMenuKeyboard,
                cancellationToken: cancellationToken);
        }
    }

    private async Task ShowMainMenuMessageAsync(
        ITelegramBotClient botClient,
        Chat chat,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            chatId: chat.Id,
            text: "👋 *Menu principale*\n\nScegli un'operazione:",
            parseMode: ParseMode.Markdown,
            replyMarkup: flowController.Value.MainMenuKeyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ShowMainMenuMessageAsFlowMessageAsync(
        ITelegramBotClient botClient,
        ConversationState state,
        Chat chat,
        CancellationToken cancellationToken)
    {
        await botClient.SendFlowMessageAsync(
            chatId: chat.Id,
            state,
            text: "👋 *Menu principale*\n\nScegli un'operazione:",
            parseMode: ParseMode.Markdown,
            replyMarkup: flowController.Value.MainMenuKeyboard,
            cancellationToken: cancellationToken);
    }
}
