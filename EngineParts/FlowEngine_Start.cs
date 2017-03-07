using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowInstanceLib.Actions;
using EnouFlowOrgMgmtLib;

namespace EnouFlowEngine
{
  public partial class FlowEngine
  {
    public FlowActionStartResult // start
      processActionRequest(FlowActionStart req)
    {
      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = db.flowInstances.Create();
        flowInst.flowTemplateId = req.concreteMetaObj.flowTemplateId;
        flowInst.flowTemplateJson = FlowTemplateDBHelper.getFlowTemplate(
          flowInst.flowTemplateId).flowTemplateJson;
        var flowDefHelper = new FlowTemplateDefHelper(
          flowInst.flowTemplateJson);
        flowInst.creatorId = req.concreteMetaObj.userId;
        flowInst.code = req.concreteMetaObj.code;
        flowInst.processingState = EnumFlowInstanceProcessingState.waitingActionRequest;
        flowInst.lifeState = EnumFlowInstanceLifeState.start;
        // 此处TimeStamp不能直接取Now, 因为启动流程时会几乎同时生成两个
        // ActionRequest, 否则第二个ActionRequest的bizTimeStamp就会马上过期
        flowInst.bizTimeStamp = flowInst.mgmtTimeStamp = req.createTime; 
        flowInst.currentActivityGuid = req.concreteMetaObj.currentActivityGuid;
        flowInst.currentActivityName = flowDefHelper.getNodeFromGuid(
          flowInst.currentActivityGuid).name;
        flowInst.startActivityGuid = flowInst.currentActivityGuid;
        flowInst.bizDataPayloadJson = req.bizDataPayloadJson;
        flowInst.bizDocumentGuid = req.bizDocumentGuid;
        flowInst.bizDocumentTypeCode = req.bizDocumentTypeCode;

        db.flowInstances.Add(flowInst);

        // 新建的流程需要回填对应的处理请求对象关于流程实例的信息
        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        reqInDb.flowInstance = flowInst;
        reqInDb.flowInstanceGuid = flowInst.guid;
        updateReqProcessingResultInDB(reqInDb,
          EnumFlowActionRequestResultType.success);
#warning TODO: Add FlowInstanceFriendlyLog & FlowInstanceTechLog

#warning TODO: Add FlowTaskForUser 是否需要???

        db.SaveChanges();

        return new FlowActionStartResult(
          req.flowActionRequestId,
          req.clientRequestGuid,
          flowInst);
      }
    }
  }
}
