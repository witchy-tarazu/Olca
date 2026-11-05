exec((r/'system-stage41-common.py').read_text())
def n(m,label):return script('NewGame41Proof',m,label)
def x(m,label,args=None):
 a={'file':'AgentScripts/Outside31Proof.cs','entry':'Outside31Legacy.'+m}
 if args is not None:a['args']=json.dumps(args)
 return call('run_script',a,label)['result']
def tap(name,label):return x('Tap',label,[name])
def stable(a,b):
 keys=['root','session','runtime','core','position','height','frame','clock','gauge','camera','cards','remaining','generation','revision','owned']
 assert all(a[k]==b[k] for k in keys),{'before':a,'after':b}
fresh=n('FreshDefaults','fresh-defaults');n('RegisterCardFixture','fixture-only-card-registration');x('Remember','remember-old-hub-inputs');tap('OutsideExplorationEntry','actual-entry');script('Outside31Proof','Move','actual-movement',[1,700]);n('FreezeAndSpendFixture','fixture-only-gauge-spend');old=n('Remember','remember-old-generation');x('RememberBack','remember-old-back');capture('final-before-newgame');tap('CloseOutsideExploration','return-before-cancel');closed=n('State','closed');filesBefore=hashes()
tap('NewGameButton','cancel-request');script('Stage13Proof','Remember','remember-canceled-confirm');tap('CancelProgressAction','actual-cancel');script('Stage13Proof','OldConfirm','old-canceled-confirm');cancel=n('State','after-cancel');stable(closed,cancel);assert hashes()==filesBefore
script('Stage13Proof','FailNextSave','inject-isolated-save-failure');tap('NewGameButton','failure-request');script('Stage13Proof','Remember','remember-retry-confirm');tap('ConfirmNewGame','actual-failed-save');failed=n('State','after-failed-save');assert failed['pending'];stable(closed,failed);assert hashes()==filesBefore
script('Stage13Proof','CancelDisabled','pending-cannot-cancel');tap('ConfirmNewGame','same-ticket-save-retry');success=n('State','after-success');ended=n('OldState','old-pair-ended');assert not success['pending'] and success['generation']!=closed['generation'] and success['revision']==closed['revision']+1
assert all(success[k]!=old[k] for k in ['session','runtime','core']);assert success['root']==old['root'] and success['owned']==old['owned']
assert ended['ended'] and ended['runtimeDisposed'] and ended['driverNull'] and ended['inputNull'] and not ended['sameSession'] and not ended['sameRuntime']
for k in ['position','height','frame','clock','gauge','gaugeMax','cards','camera']:assert success[k]==fresh[k],(k,success,fresh)
restored=script('Stage13Proof','RestoreStore','restore-isolated-store');assert restored['writes']==2
filesAfter=hashes();script('Stage13Proof','OldConfirm','old-confirm-after-success');afterOld=n('State','after-old-confirm');stable(success,afterOld);assert hashes()==filesAfter
# Permanent host chrome remains a current valid control; replay only invalidated Dialog/View callbacks.
tap('OutsideExplorationEntry','fresh-outside-entry');entered=n('State','fresh-entered');assert entered['presented'];stable(success,entered);x('OldBack','old-back-after-success');stable(entered,n('State','after-old-back'));capture('final-after-newgame')
# Normal View recreation/reentry retains the new pair, not a second reset.
x('DestroyView','natural-destroy-recovered');recreated=n('State','view-recreated');stable(entered,recreated);tap('CloseOutsideExploration','normal-return');tap('OutsideExplorationEntry','normal-reenter');stable(entered,n('State','normal-reenter-state'));tap('CloseOutsideExploration','return-before-held-entry')
# Current generation's held-key boundary and actual new movement.
script('NewGame41Proof','Freeze','keep-zero-before-host');h('Remember','normal-host-remember');h('Close','normal-host-close',[True]);x('OldBack','old-back-host-closed') if False else None
h('Reopen','normal-host-reopen');n('Freeze','freeze-host-reopened');host=n('State','host-reopened-state');stable(success,host)
# Explicitly enable clock after host reopen, then test held input before entry.
script('Stage31Proof','Release','release-old-hub-references');n('Unfreeze','unfreeze-before-held-entry');script('Outside31Proof','HeldEntry','held-input-on-new-generation');held=n('State','held-entry-state');assert held['session']==success['session'] and held['position']==fresh['position'] and held['height']==0
script('Outside31Proof','Move','new-generation-actual-movement',[-1,450]);n('Freeze','freeze-moved-new-generation');moved=n('State','new-generation-moved');assert moved['session']==success['session'] and moved['position']!=fresh['position'];endedAfter=n('OldState','old-runtime-remains-ended');assert endedAfter['frame']==ended['frame'] and endedAfter['clock']==ended['clock'] and endedAfter['position']==ended['position']
assert hashes()==filesAfter
(e/'system-stage41-native-proof.json').write_text(json.dumps({'reproduction':'system-stage41-actual-reproduction.json','fixtureOnly':['initial hand registration via existing API','20 points gauge perturbation; no product card-use implementation'],'before':old,'cancel':cancel,'failedSave':failed,'success':success,'oldEnded':ended,'newEntered':entered,'normalHostReentry':host,'heldInput':held,'newMovement':moved,'oldStillEnded':endedAfter,'saveFailureRetryWrites':restored['writes'],'filesAfterSuccess':filesAfter,'sourceHashes':json.loads((e/'system-stage41-v1-source-hashes.json').read_text())},ensure_ascii=False,indent=2));print('Final actual cancel/fail/retry/new-pair and normal reentry checks passed',flush=True)
tap('CloseOutsideExploration','final-return');n('Release','release-old-pair-proof');x('Release','release-outside-events');script('Stage13Proof','Release','release-old-confirm');h('Release','release-prior-host-proof');h('Remember','cleanup-host-remember');h('Close','cleanup-host-close',[True]);h('Release','cleanup-host-release');call('editor_stop',{},'final-stop-once');print('Final Stop requested once; collect readiness',flush=True)
