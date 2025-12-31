namespace ExpenseTracker.TelegramBot;

public enum ConversationStep
{
    MainMenu,
    SelectCategory,
    SelectSubCategory,
    EnterDescription,
    EnterAmount
}

public class ConversationState
{
    public ConversationStep Step { get; set; } = ConversationStep.MainMenu;
    public int? SelectedCategoryId { get; set; }
    public string? SelectedCategoryName { get; set; }
    public int? SelectedSubCategoryId { get; set; }
    public string? SelectedSubCategoryName { get; set; }
    public string? Description { get; set; }

    public void Reset()
    {
        Step = ConversationStep.MainMenu;
        SelectedCategoryId = null;
        SelectedCategoryName = null;
        SelectedSubCategoryId = null;
        SelectedSubCategoryName = null;
        Description = null;
    }
}

