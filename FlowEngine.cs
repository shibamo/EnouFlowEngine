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
      FlowInstance flowInst, EnouFlowInstanceContext db, out string failReason)
    {
      if (bizTimeStamp < flowInst.bizTimeStamp)
      {
        failReason = "bizTimeStamp expired, 业务时间戳已过期无效.";

        var reqInDb = db.flowActionRequests.Find(req.flowActionRequestId);
        reqInDb.isProcessed = true;
        reqInDb.finishTime = DateTime.Now;
        reqInDb.failReason = failReason;

        db.SaveChanges();
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
      task.flowInstance = flowInst;
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
        t.flowInstance.flowInstanceId == flowInst.flowInstanceId && 
        t.bizTimeStamp == bizTimeStamp && 
        t.currentActivityGuid == currentActivityGuid).ToList();

      tasks.ForEach(t => {
        if (t.userId != actionUserId)
        {
          t.taskState = EnumFlowTaskState.obsoleted;
        }
        else
        {
          t.finishTime = newBizTimeStamp;
          t.taskState = EnumFlowTaskState.done;
        }
      });

      //db.SaveChanges();
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
