using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>财务-支出端点（D4-4，UC-FIN-005/006，BR-FIN-04/10）。</summary>
    [RoutePrefix("api/v1/expenses")]
    public class ExpensesController : ApiController
    {
        private readonly ExpenseService _expenses;

        public ExpensesController()
        {
            _expenses = new ExpenseService();
        }

        // ---------- 支出分类（UC-FIN-006） ----------
        [HttpGet]
        [Route("categories")]
        public ApiResponse<List<ExpenseCategoryDto>> ListCategories()
        {
            return ApiResponse<List<ExpenseCategoryDto>>.Ok(_expenses.ListCategories());
        }

        [HttpPost]
        [Route("categories")]
        public ApiResponse<ExpenseCategoryDto> CreateCategory(ExpenseCategoryRequest request)
        {
            return ApiResponse<ExpenseCategoryDto>.Ok(_expenses.CreateCategory(request));
        }

        [HttpPut]
        [Route("categories/{id:int}")]
        public ApiResponse<ExpenseCategoryDto> UpdateCategory(int id, ExpenseCategoryRequest request)
        {
            return ApiResponse<ExpenseCategoryDto>.Ok(_expenses.UpdateCategory(id, request));
        }

        [HttpDelete]
        [Route("categories/{id:int}")]
        public ApiResponse<object> DeleteCategory(int id)
        {
            _expenses.DeleteCategory(id);
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 支出登记（UC-FIN-005） ----------
        [HttpPost]
        [Route("")]
        public ApiResponse<ExpenseDto> CreateExpense(ExpenseCreateRequest request)
        {
            return ApiResponse<ExpenseDto>.Ok(_expenses.CreateExpense(request));
        }

        [HttpGet]
        [Route("")]
        public ApiResponse<PageResult<ExpenseDto>> QueryExpenses([FromUri] PageRequest request)
        {
            return ApiResponse<PageResult<ExpenseDto>>.Ok(_expenses.QueryExpenses(request));
        }

        [HttpPut]
        [Route("{id:int}")]
        public ApiResponse<ExpenseDto> UpdateExpense(int id, ExpenseCreateRequest request)
        {
            return ApiResponse<ExpenseDto>.Ok(_expenses.UpdateExpense(id, request));
        }

        [HttpDelete]
        [Route("{id:int}")]
        public ApiResponse<object> DeleteExpense(int id)
        {
            _expenses.DeleteExpense(id);
            return ApiResponse<object>.Ok(null);
        }
    }
}