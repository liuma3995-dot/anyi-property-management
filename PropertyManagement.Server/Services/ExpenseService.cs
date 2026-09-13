using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 支出服务（D4-4，UC-FIN-005/006，BR-FIN-04/05/10）：
    /// 支出分类维护（被引用禁删可停用）；支出登记/修改/软删除，关联对象可空（M4 简单版）。
    /// </summary>
    public class ExpenseService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IFinanceRepository _finance;
        private readonly AuditService _audit;

        public ExpenseService()
            : this(new SqliteConnectionFactory(), new SqlFinanceRepository(), new AuditService())
        {
        }

        public ExpenseService(IDbConnectionFactory connectionFactory, IFinanceRepository finance, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _finance = finance;
            _audit = audit;
        }

        // ---------- 支出分类（UC-FIN-006） ----------
        public List<ExpenseCategoryDto> ListCategories()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.ListExpenseCategories(connection);
            }
        }

        public ExpenseCategoryDto CreateCategory(ExpenseCategoryRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                throw ApiException.BadRequest("支出分类名称不能为空");
            }

            var category = new ExpenseCategoryDto
            {
                Name = request.Name.Trim(),
                CategoryType = request.CategoryType,
                Status = 0
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                category.Id = _finance.InsertExpenseCategory(connection, transaction, category);
                transaction.Commit();
            }

            _audit.Write("EXPENSE_CATEGORY_CREATE", "expense_category", category.Id.ToString(),
                "新增支出分类：" + category.Name,
                userName: operatorName, ip: ip, result: "成功");
            return category;
        }

        public ExpenseCategoryDto UpdateCategory(int id, ExpenseCategoryRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                throw ApiException.BadRequest("支出分类名称不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ExpenseCategoryDto existing = _finance.GetExpenseCategory(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("支出分类不存在或已删除");
                }

                existing.Name = request.Name.Trim();
                existing.CategoryType = request.CategoryType;
                _finance.UpdateExpenseCategory(connection, transaction, existing);
                transaction.Commit();

                _audit.Write("EXPENSE_CATEGORY_UPDATE", "expense_category", id.ToString(),
                    "修改支出分类：" + existing.Name,
                    userName: operatorName, ip: ip, result: "成功");
                return existing;
            }
        }

        public void DeleteCategory(int id, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ExpenseCategoryDto existing = _finance.GetExpenseCategory(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("支出分类不存在或已删除");
                }

                // BR-COM-05 / UC-FIN-006 E1：已被支出引用 → 禁止删除，可停用
                if (_finance.ExpenseCategoryReferenced(connection, id))
                {
                    throw ApiException.Conflict("该支出分类已被支出记录引用，不能删除；可将其停用");
                }

                _finance.SoftDeleteExpenseCategory(connection, transaction, id);
                transaction.Commit();

                _audit.Write("EXPENSE_CATEGORY_DELETE", "expense_category", id.ToString(),
                    "删除支出分类：" + existing.Name,
                    userName: operatorName, ip: ip, result: "成功");
            }
        }

        // ---------- 支出登记（UC-FIN-005） ----------
        public ExpenseDto CreateExpense(ExpenseCreateRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.CategoryId <= 0)
            {
                throw ApiException.BadRequest("必须选择支出分类（BR-FIN-04）");
            }
            if (request.Amount <= 0)
            {
                throw ApiException.ValidationFailed("支出金额必须大于 0");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ExpenseCategoryDto category = _finance.GetExpenseCategory(connection, request.CategoryId);
                if (category == null)
                {
                    throw ApiException.NotFound("支出分类不存在或已停用");
                }

                var expense = new ExpenseDto
                {
                    CategoryId = request.CategoryId,
                    CategoryName = category.Name,
                    Amount = request.Amount,
                    ExpenseDate = request.ExpenseDate == default(DateTime) ? DateTime.Now : request.ExpenseDate,
                    Note = request.Note,
                    Payee = request.Payee,
                    Status = 0
                };
                expense.Id = _finance.InsertExpense(connection, transaction, expense);

                if (request.Objects != null && request.Objects.Count > 0)
                {
                    _finance.InsertExpenseObjectRels(connection, transaction, expense.Id, request.Objects);
                }

                transaction.Commit();

                _audit.Write("EXPENSE_CREATE", "expense", expense.Id.ToString(),
                    "登记支出：" + category.Name + "，" + expense.Amount.ToString("0.00") + " 元",
                    userName: operatorName, ip: ip, result: "成功");
                return expense;
            }
        }

        public ExpenseDto UpdateExpense(int id, ExpenseCreateRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.CategoryId <= 0)
            {
                throw ApiException.BadRequest("必须选择支出分类（BR-FIN-04）");
            }
            if (request.Amount <= 0)
            {
                throw ApiException.ValidationFailed("支出金额必须大于 0");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ExpenseDto existing = _finance.GetExpense(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("支出记录不存在或已删除");
                }
                if (_finance.GetExpenseCategory(connection, request.CategoryId) == null)
                {
                    throw ApiException.NotFound("支出分类不存在或已停用");
                }

                existing.CategoryId = request.CategoryId;
                existing.Amount = request.Amount;
                existing.ExpenseDate = request.ExpenseDate == default(DateTime) ? DateTime.Now : request.ExpenseDate;
                existing.Note = request.Note;
                existing.Payee = request.Payee;
                _finance.UpdateExpense(connection, transaction, existing);
                transaction.Commit();

                _audit.Write("EXPENSE_UPDATE", "expense", id.ToString(),
                    "修改支出：" + existing.Amount.ToString("0.00") + " 元",
                    userName: operatorName, ip: ip, result: "成功");
                return existing;
            }
        }

        public void DeleteExpense(int id, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ExpenseDto existing = _finance.GetExpense(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("支出记录不存在或已删除");
                }

                // BR-FIN-10：涉及金额记录软删除，保留操作轨迹
                _finance.SoftDeleteExpense(connection, transaction, id);
                transaction.Commit();

                _audit.Write("EXPENSE_DELETE", "expense", id.ToString(),
                    "删除支出：" + existing.Amount.ToString("0.00") + " 元（软删除 BR-FIN-10）",
                    userName: operatorName, ip: ip, result: "成功");
            }
        }

        public PageResult<ExpenseDto> QueryExpenses(PageRequest query)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                int total;
                List<ExpenseDto> items = _finance.ListExpenses(connection, query ?? new PageRequest(), out total);
                return new PageResult<ExpenseDto>
                {
                    PageIndex = query == null ? 1 : query.PageIndex,
                    PageSize = query == null ? 20 : query.PageSize,
                    Total = total,
                    Items = items
                };
            }
        }
    }
}
