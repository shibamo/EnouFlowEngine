using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
      EnumFlowTaskType taskType= EnumFlowTaskType.normal)
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
      string currentActivityGuid,FlowInstance flowInst)
    {
      var tasks = db.flowTaskForUsers.Where(t =>
        t.FlowInstance.flowInstanceId == flowInst.flowInstanceId && 
        t.bizTimeStamp == bizTimeStamp && 
        t.currentActivityGuid == currentActivityGuid).ToList();

      tasks.ForEach(t => {
        if (t.userId != actionUserId)
        {
          // 其他用户已删除该任务或是已完成的邀请提供处理意见的任务,不需要标记为过期
          if (t.taskState!= EnumFlowTaskState.deletedByUser && t.taskState!= EnumFlowTaskState.done) 
          { 
            t.taskState = EnumFlowTaskState.obsoleted;
          }
        }
        else
        {
          t.finishTime = newBizTimeStamp;
          t.taskState = EnumFlowTaskState.done;
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
  }
}
