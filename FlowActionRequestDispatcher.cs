﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowInstanceLib.Actions;
using EnouFlowOrgMgmtLib;
using EnouFlowEngine;

namespace EnouFlowEngine
{
  public class FlowActionRequestDispatcher
  {
    public FlowActionResult processNextAction()
    {
      var req = FlowInstanceHelper.GetFirtUnprocessedRequest();
      if (req == null) return null;

      return processAction(req);
    }

    public FlowActionResult processNextActionOfSpecifiedInstance(
      int flowInstanceId, EnumFlowActionRequestType[] flowActionRequestTypes)
    {
      var req = FlowInstanceHelper.GetFirtUnprocessedRequest(
        flowInstanceId, flowActionRequestTypes);
      if (req == null) return null;

      return processAction(req);
    }

    public FlowActionResult processSpecifiedAction(
      int flowActionRequestId, bool onlyNotProcessed = true)
    {
      var req = FlowInstanceHelper.GetFlowAction(
        flowActionRequestId, onlyNotProcessed);
      if (req == null) return null;

      return processAction(req);
    }

    private FlowActionResult processAction(FlowAction req)
    {
      if (req == null) return null;

      var engine = new FlowEngine();
      FlowActionResult result = null;

      switch (req.requestType)
      {
        case EnumFlowActionRequestType.start:
          result = engine.processActionRequest((FlowActionStart)req);
          break;

        case EnumFlowActionRequestType.moveTo:
          result = engine.processActionRequest((FlowActionMoveTo)req);
          break;

        case EnumFlowActionRequestType.moveToAutoGenerated:
          result = engine.processActionRequest((FlowActionMoveToAutoGenerated)req);
          break;

        case EnumFlowActionRequestType.rejectToStart:
          result = engine.processActionRequest((FlowActionRejectToStart)req);
          break;

        default:
          throw new Exception(req.requestType.ToString() + 
            " of EnumFlowActionRequestType not implemented !");
      }

      return result;
    }


  }
}
