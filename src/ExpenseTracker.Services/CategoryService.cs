using ExpenseTracker.Data;
using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services;

public class CategoryService(AppDbContext context)
{
    public async Task<List<Category>> GetAllCategoriesAsync() =>
        await context.Categories
            .OrderBy(c => c.Name)
            .ToListAsync();

    public async Task<Category?> GetCategoryByNameAsync(string name) =>
        await context.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());

    public async Task<List<SubCategory>> GetSubCategoriesByCategoryIdAsync(int categoryId) =>
        await context.SubCategories
            .Where(sc => sc.CategoryId == categoryId)
            .OrderBy(sc => sc.Name)
            .ToListAsync();
}