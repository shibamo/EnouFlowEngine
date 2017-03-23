using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using System.Reflection;
using System.Dynamic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowInstanceLib.Actions;
using EnouFlowOrgMgmtLib;

namespace EnouFlowEngine
{
  public partial class FlowEngine
  {
    private static Logger logger = LogManager.GetCurrentClassLogger();

    private bool isBizTimeStampValid(DateTime bizTimeStamp, FlowAction req,
      FlowInstance flowInst, out string failReason)
    {
      if (bizTimeStamp < flowInst.bizTimeStamp)
      {
        failReason = "bizTimeStamp expired, 业务时间戳已过期无效.";

        //修改,根据函数名称,只检查,不更新数据库状态,由外部调用者自行更新
        //var reqInDb = db.flowActionRequests.Find(req.flowActionRequestId);
        //reqInDb.isProcessed = true;
        //reqInDb.finishTime = DateTime.Now;
        //reqInDb.failReason = failReason;

        //db.SaveChanges();
        return false;
      }

      failReason = null;
      return true;
    }

    private FlowActionRequest getReqInDB(int id, EnouFlowInstanceContext db)
    {
      return db.flowActionRequests.Find(id);
    }

    private void updateReqProcessingResultInDB(FlowActionRequest reqInDb,
      EnumFlowActionRequestResultType resultType, string failReason = null)
    {
      reqInDb.isProcessed = true;
      reqInDb.finishTime = DateTime.Now;
      reqInDb.resultType = resultType;
      reqInDb.failReason = failReason;
    }

    private bool updateBizDataPayloadJsonOfFlowInst(FlowInstance flowInst,
      FlowAction req)
    {
      if (!string.IsNullOrEmpty(req.bizDataPayloadJson))
      {
        flowInst.bizDataPayloadJson = req.bizDataPayloadJson;
        return true;
      }
      return false;
    }

    private FlowTaskForUser addFlowTaskForUser(EnouFlowInstanceContext db,
      UserDTO user, FlowInstance flowInst,
      EnumFlowTaskType taskType = EnumFlowTaskType.normal)
    {
      var task = db.flowTaskForUsers.Create();

      task.userId = user.userId;
      task.userGuid = user.guid;
      task.FlowInstance = flowInst;
      task.bizDocumentGuid = flowInst.bizDocumentGuid;
      task.bizDocumentTypeCode = flowInst.bizDocumentTypeCode;
      task.bizTimeStamp = flowInst.bizTimeStamp;
      task.currentActivityGuid = flowInst.currentActivityGuid;
      task.taskType = taskType;
      db.flowTaskForUsers.Add(task);

      return task;
    }

    private void updateTaskForUserStatesAfterAction(EnouFlowInstanceContext db,
      int actionUserId, DateTime bizTimeStamp, DateTime newBizTimeStamp,
      string currentActivityGuid, FlowInstance flowInst, FlowActionRequest reqInDb)
    {
      var tasks = db.flowTaskForUsers.Where(t =>
        t.FlowInstance.flowInstanceId == flowInst.flowInstanceId &&
        t.bizTimeStamp == bizTimeStamp &&
        t.currentActivityGuid == currentActivityGuid).ToList();

      tasks.ForEach(t =>
      {
        if (t.userId != actionUserId)
        {
          // 其他用户已删除该任务或是已完成的邀请提供处理意见的任务,不需要标记为过期
          if (t.taskState != EnumFlowTaskState.deletedByUser && t.taskState != EnumFlowTaskState.done)
          {
            t.taskState = EnumFlowTaskState.obsoleted;
          }
        }
        else
        {
          t.finishTime = newBizTimeStamp;
          t.taskState = EnumFlowTaskState.done;
          t.FlowActionRequest = reqInDb;
          t.delegateeUserId = reqInDb.delegateeUserId; // 有可能是被任务代理人完成
          t.delegateeUserGuid = reqInDb.delegateeUserGuid;
        }
      });

      //db.SaveChanges();
    }

    private void updateRequestToSuccess(FlowActionRequest reqInDb,
      FlowInstance flowInst)
    {
      reqInDb.isProcessed = true;
      reqInDb.finishTime = DateTime.Now;
      reqInDb.resultType = EnumFlowActionRequestResultType.success;
      if (reqInDb.flowInstance == null || reqInDb.flowInstanceGuid == null)
      {
        reqInDb.flowInstance = flowInst;
        reqInDb.flowInstanceGuid = flowInst.guid;
      }
    }

    private FlowInstance getFlowInstance(EnouFlowInstanceContext db,
      int flowInstanceId, string bizDocumentGuid)
    {
      FlowInstance flowInst = db.flowInstances.Find(flowInstanceId);
      if (flowInst == null)
      {
        flowInst = db.flowInstances.Where(
          inst => inst.bizDocumentGuid == bizDocumentGuid)
          .ToList().FirstOrDefault();
      }

      return flowInst;
    }

    private static List<UserDTO> getUserDTOsFromPaticipantList(
      List<Paticipant> paticipants, FlowInstance flowInstance)
    {
      List<UserDTO> result = new List<UserDTO>();

      if (paticipants?.Count() > 0)
      {
        paticipants.ForEach(p =>
        {
          if (p != null)
          {
            result.AddRange(resolvePaticipantToUserDTOs(p, flowInstance));
          }
        });
        result = result.Distinct().ToList();
      }

      return result;
    }

    private static List<UserDTO> resolvePaticipantToUserDTOs(
      Paticipant paticipant, FlowInstance flowInstance)
    {
      if (paticipant.PaticipantType == "user") // 用户
      {
        var result = new List<UserDTO>();
        using (var db = new EnouFlowOrgMgmtContext())
        {
          result.Add(OrgMgmtDBHelper.getUserDTO((int)paticipant.PaticipantObj.userId, db));
        }

        return result;
      }

      if (paticipant.PaticipantType == "role") // 角色
      {
        using (var db = new EnouFlowOrgMgmtContext())
        {
          return OrgMgmtDBHelper.getUserDTOsOfRole((int)paticipant.PaticipantObj.roleId, db);
        }
      }

      if (paticipant.PaticipantType == "dynamic") // 流程动态用户
      {
        FlowDynamicUser flowDynamicUser;
        using (var db = new EnouFlowTemplateDbContext())
        {
          flowDynamicUser = db.flowDynamicUsers.Find(
            paticipant.PaticipantObj.flowDynamicUserId);
        }
        if (flowDynamicUser != null)
        {
          return ResolveFlowDynamicUser(
            flowDynamicUser.script, flowDynamicUser.name, flowInstance);
        }
      }

      return new List<UserDTO>();
    }

    /// <summary>
    /// 通过脚本动态确定用户列表
    /// </summary>
    /// <param name="script"></param>
    /// <param name="flowDynamicUserName"></param>
    /// <param name="flowInstance"></param>
    /// <returns></returns>
    private static List<UserDTO> ResolveFlowDynamicUser(
      string script, string flowDynamicUserName, FlowInstance flowInstance)
    {
      #region 初始化执行环境
      var _session = new DictionaryContext();
      _session.globals.Add("flowInstance", flowInstance);
      var _references = new Assembly[] { typeof(DictionaryContext).Assembly,
                             typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly,
                             typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly,
                             typeof(ExpandoObject).Assembly,
                             typeof(JsonConvert).Assembly,
                             typeof(ExpandoObjectConverter).Assembly,
                             typeof(Paticipant).Assembly,
                             typeof(UserDTO).Assembly,
                             typeof(FlowInstance).Assembly,
                             typeof(List<>).Assembly
      };
      var _imports = new string[] {typeof(DictionaryContext).Namespace,
                          typeof(System.Runtime.CompilerServices.DynamicAttribute).Namespace,
                          typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Namespace,
                          typeof(ExpandoObject).Namespace,
                          typeof(JsonConvert).Namespace,
                          typeof(ExpandoObjectConverter).Namespace,
                          typeof(Paticipant).Namespace,
                          typeof(UserDTO).Namespace,
                          typeof(FlowInstance).Namespace,
                          typeof(List<>).Namespace
      };
      var _options = ScriptOptions.Default
        .AddReferences(_references)
        .AddImports(_imports);
      #endregion

      #region 执行
      try
      {
        var result = CSharpScript.RunAsync(script,
          globals: _session, options: _options).Result.ReturnValue;
        return (List<UserDTO>)result;
      }
      catch (Exception ex) // 默默记录到日志并继续执行
      {
        var _ex = ex;
        logger.Error(ex.Message,
          "运行脚本解析流程动态用户出错, 流程实例id:{0}; 流程动态用户名:{1}; ",
          flowInstance.flowInstanceId, flowDynamicUserName);
        return null;
      }
      #endregion
    }

    private static void addFlowInstanceFriendlyLog(
      FlowInstance flowInstance, int flowActionRequestId,
      string currentActivityName, int paticipantUserId,
      int? delegateeUserId, string actionName, 
      string paticipantMemo, EnouFlowInstanceContext db)
    {
      var log = db.flowFriendlyLogs.Create();

      log.FlowInstance = flowInstance;
      log.flowActionRequestId = flowActionRequestId;
      log.currentActivityName = currentActivityName;
      var paticipant = OrgMgmtDBHelper.getUserDTO(
        paticipantUserId);
      log.paticipantName = paticipant.name + 
        paticipant.englishName==null? "" : "-" + paticipant.englishName;
      if (delegateeUserId.HasValue)
      {
        var delegatee = OrgMgmtDBHelper.getUserDTO(
        delegateeUserId.Value);
        log.delegateeName = delegatee.name +
          delegatee.englishName == null ? "" : "-" + delegatee.englishName;
      }
      log.actionName = actionName;
      log.paticipantMemo = paticipantMemo;

      db.flowFriendlyLogs.Add(log);
    }

  }

  public sealed class DictionaryContext
  {
    public DictionaryContext()
    {
      globals = new Dictionary<string, object>();
    }
    public Dictionary<string, object> globals { get; }
  }
}
