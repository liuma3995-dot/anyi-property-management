using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Api.Middleware;
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
            return ApiResponse<ExpenseCategoryDto>.Ok(_expenses.CreateCategory(request, GetUsername(), GetIp()));
        }

        [HttpPut]
        [Route("categories/{id:int}")]
        public ApiResponse<ExpenseCategoryDto> UpdateCategory(int id, ExpenseCategoryRequest request)
        {
            return ApiResponse<ExpenseCategoryDto>.Ok(_expenses.UpdateCategory(id, request, GetUsername(), GetIp()));
        }

        [HttpDelete]
        [Route("categories/{id:int}")]
        public ApiResponse<object> DeleteCategory(int id)
        {
            _expenses.DeleteCategory(id, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 支出登记（UC-FIN-005） ----------
        [HttpPost]
        [Route("")]
        public ApiResponse<ExpenseDto> CreateExpense(ExpenseCreateRequest request)
        {
            return ApiResponse<ExpenseDto>.Ok(_expenses.CreateExpense(request, GetUsername(), GetIp()));
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
            return ApiResponse<ExpenseDto>.Ok(_expenses.UpdateExpense(id, request, GetUsername(), GetIp()));
        }

        [HttpDelete]
        [Route("{id:int}")]
        public ApiResponse<object> DeleteExpense(int id)
        {
            _expenses.DeleteExpense(id, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        /// <summary>操作人（BR-ORG-01 审计八列）：从鉴权中间件写入的 OWIN 环境读取。</summary>
        private string GetUsername()
        {
            object value;
            if (Request.Properties.TryGetValue("MS_OwinContext", out value))
            {
                var owinContext = value as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Get<string>(AuthMiddleware.UsernameEnvKey) ?? string.Empty;
                }
            }
            return string.Empty;
        }

        /// <summary>客户端 IP（BR-ORG-01 审计八列）。</summary>
        private string GetIp()
        {
            object value;
            if (Request.Properties.TryGetValue("MS_OwinContext", out value))
            {
                var owinContext = value as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Request.RemoteIpAddress;
                }
            }
            return null;
        }
    }
}
