using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>便民电话簿端点（M6 D6-3，UC-TEL-001~006 + BR-TEL-01~04）。</summary>
    [RoutePrefix("api/v1/phonebook")]
    public class PhoneBookController : ApiController
    {
        private readonly PhoneBookService _service;

        public PhoneBookController()
        {
            _service = new PhoneBookService();
        }

        [HttpGet] [Route("categories")]
        public ApiResponse<List<PhoneCategoryDto>> ListCategories() =>
            ApiResponse<List<PhoneCategoryDto>>.Ok(_service.ListCategories());

        [HttpPost] [Route("categories")]
        public ApiResponse<PhoneCategoryDto> CreateCategory(PhoneCategoryRequest request) =>
            ApiResponse<PhoneCategoryDto>.Ok(_service.SaveCategory(0, request));

        [HttpPut] [Route("categories/{id:int}")]
        public ApiResponse<PhoneCategoryDto> UpdateCategory(int id, PhoneCategoryRequest request) =>
            ApiResponse<PhoneCategoryDto>.Ok(_service.SaveCategory(id, request));

        [HttpPost] [Route("categories/{id:int}/delete")]
        public ApiResponse<bool> DeleteCategory(int id)
        {
            _service.DeleteCategory(id);
            return ApiResponse<bool>.Ok(true);
        }

        [HttpGet] [Route("types")]
        public ApiResponse<List<PhoneTypeDto>> ListTypes() =>
            ApiResponse<List<PhoneTypeDto>>.Ok(_service.ListTypes());

        [HttpPost] [Route("types")]
        public ApiResponse<PhoneTypeDto> CreateType(PhoneTypeRequest request) =>
            ApiResponse<PhoneTypeDto>.Ok(_service.SaveType(0, request));

        [HttpPut] [Route("types/{id:int}")]
        public ApiResponse<PhoneTypeDto> UpdateType(int id, PhoneTypeRequest request) =>
            ApiResponse<PhoneTypeDto>.Ok(_service.SaveType(id, request));

        [HttpPost] [Route("types/{id:int}/delete")]
        public ApiResponse<bool> DeleteType(int id)
        {
            _service.DeleteType(id);
            return ApiResponse<bool>.Ok(true);
        }

        [HttpGet] [Route("entries")]
        public ApiResponse<PageResult<PhoneEntryDto>> QueryEntries([FromUri] PhoneEntryQueryRequest request) =>
            ApiResponse<PageResult<PhoneEntryDto>>.Ok(_service.QueryEntries(request ?? new PhoneEntryQueryRequest()));

        [HttpGet] [Route("entries/{id:int}")]
        public ApiResponse<PhoneEntryDto> GetEntry(int id) => ApiResponse<PhoneEntryDto>.Ok(_service.GetEntry(id));

        [HttpPost] [Route("entries")]
        public ApiResponse<PhoneEntryDto> CreateEntry(PhoneEntryRequest request) =>
            ApiResponse<PhoneEntryDto>.Ok(_service.SaveEntry(0, request));

        [HttpPut] [Route("entries/{id:int}")]
        public ApiResponse<PhoneEntryDto> UpdateEntry(int id, PhoneEntryRequest request) =>
            ApiResponse<PhoneEntryDto>.Ok(_service.SaveEntry(id, request));

        [HttpPost] [Route("entries/{id:int}/status")]
        public ApiResponse<PhoneEntryDto> SetEntryStatus(int id, PhoneEntryStatusRequest request) =>
            ApiResponse<PhoneEntryDto>.Ok(_service.SetEntryStatus(id, request ?? new PhoneEntryStatusRequest { Status = PhoneEntryStatus.Enabled }));

        [HttpPost] [Route("entries/batch-disable")]
        public ApiResponse<PhoneEntryBatchStatusResultDto> BatchDisable(PhoneEntryBatchStatusRequest request) =>
            ApiResponse<PhoneEntryBatchStatusResultDto>.Ok(_service.BatchDisable(request ?? new PhoneEntryBatchStatusRequest()));

        [HttpPost] [Route("entries/{id:int}/top")]
        public ApiResponse<PhoneEntryDto> SetEntryTop(int id, PhoneEntryTopRequest request) =>
            ApiResponse<PhoneEntryDto>.Ok(_service.SetEntryTop(id, request != null && request.IsTop));

        [HttpPost] [Route("sync-employees")]
        public ApiResponse<EmployeeSyncResultDto> SyncEmployees(EmployeeSyncRequest request) =>
            ApiResponse<EmployeeSyncResultDto>.Ok(_service.SyncEmployees(request ?? new EmployeeSyncRequest()));
    }
}
