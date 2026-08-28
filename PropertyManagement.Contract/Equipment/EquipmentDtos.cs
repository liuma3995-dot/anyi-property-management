using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Equipment
{
    /// <summary>设备类型（t_device_type，P-04 类型默认保养周期）。</summary>
    public class DeviceTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaintenanceCycle { get; set; }
        public int Status { get; set; }
    }

    /// <summary>设备（t_device，UC-EQP-001/007，BR-EQP-01）。</summary>
    public class DeviceDto
    {
        public int Id { get; set; }
        public int TypeId { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public DeviceStatus Status { get; set; }
        public DateTime? EnableDate { get; set; }
        public bool DelFlag { get; set; }
    }

    /// <summary>保养记录（t_maintenance_record，UC-EQP-002，BR-EQP-02/03）。</summary>
    public class MaintenanceRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime MDate { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
    }

    /// <summary>年检记录（t_inspection_record，UC-EQP-003）。</summary>
    public class InspectionRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime IDate { get; set; }
        public string Result { get; set; }
    }

    /// <summary>故障记录（t_fault_record，UC-EQP-004，event_id 弱关联应急）。</summary>
    public class FaultRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int? EventId { get; set; }
        public DateTime FTime { get; set; }
        public string Symptom { get; set; }
        public string Cause { get; set; }
        public string Handle { get; set; }
    }

    /// <summary>维保单位（t_vendor，UC-EQP-005）。</summary>
    public class VendorDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>到期提醒（t_reminder，UC-EQP-006/UC-COM-006，保养/年检）。</summary>
    public class EquipmentReminderDto
    {
        public int ReminderId { get; set; }
        public string Type { get; set; }        // maintenance / inspection
        public int DeviceId { get; set; }
        public string DeviceName { get; set; }
        public DateTime DueAt { get; set; }
        public ReminderStatus Status { get; set; }
    }

    public class DeviceTypeRequest
    {
        public string Name { get; set; }
        public int MaintenanceCycle { get; set; }
    }

    /// <summary>设备登记请求（UC-EQP-001）。</summary>
    public class DeviceRequest
    {
        public int TypeId { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public DateTime? EnableDate { get; set; }
    }

    /// <summary>设备状态变更请求（BR-EQP-01：维修/停用/报废/启用）。</summary>
    public class DeviceStatusRequest
    {
        public DeviceStatus Status { get; set; }
        public string Reason { get; set; }
    }

    public class MaintenanceRecordRequest
    {
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime MDate { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
    }

    public class InspectionRecordRequest
    {
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime IDate { get; set; }
        public string Result { get; set; }
    }

    public class FaultRecordRequest
    {
        public int DeviceId { get; set; }
        public int? EventId { get; set; }
        public DateTime FTime { get; set; }
        public string Symptom { get; set; }
        public string Cause { get; set; }
        public string Handle { get; set; }
    }

    public class VendorRequest
    {
        public string Name { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>设备查询条件（UC-EQP-007，分页）。</summary>
    public class DeviceQueryRequest : PageRequest
    {
        public int? TypeId { get; set; }
        public DeviceStatus? Status { get; set; }
    }
}
