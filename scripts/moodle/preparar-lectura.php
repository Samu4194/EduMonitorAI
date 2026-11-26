<?php
// Ejecutar por stdin en el contenedor web de Moodle. La salida es un secreto.
define('CLI_SCRIPT', true);
require '/var/www/html/config.php';
require_once $CFG->dirroot . '/user/lib.php';
require_once $CFG->dirroot . '/webservice/lib.php';
\core\session\manager::set_user(get_admin());
$course = $DB->get_record('course', ['idnumber' => 'SOF-101-2026AD-A'], '*', MUST_EXIST);
$sys = context_system::instance();
$ctx = context_course::instance($course->id);
$servicekey = 'edumonitor_lectura';
$ws = new webservice();
$service = $ws->get_external_service_by_shortname($servicekey);
if (!$service) {
    $id = $ws->add_external_service((object)['name' => 'EduMonitor · lectura', 'shortname' => $servicekey,
        'restrictedusers' => 1, 'enabled' => 1, 'downloadfiles' => 0, 'uploadfiles' => 0]);
    $service = $ws->get_external_service_by_shortname($servicekey, MUST_EXIST);
}
$functions = ['core_course_get_courses_by_field','core_course_get_contents','core_enrol_get_enrolled_users','gradereport_user_get_grade_items'];
$allowed = $DB->get_fieldset_select('external_services_functions', 'functionname', 'externalserviceid=?', [$service->id]);
if (!$service->restrictedusers || array_diff($allowed, $functions)) { throw new Exception('El servicio existente tiene permisos distintos.'); }
foreach ($functions as $function) {
    if (!$ws->service_function_exists($function, $service->id)) { $ws->add_external_function_to_service($function, $service->id); }
}
$user = $DB->get_record('user', ['username' => $servicekey, 'mnethostid' => $CFG->mnet_localhost_id]);
if (!$user) {
    $id = user_create_user((object)['username' => $servicekey, 'idnumber'=>'EDUMONITOR-WS', 'auth'=>'manual',
        'confirmed'=>1,'password'=>bin2hex(random_bytes(36)).'Aa7!', 'firstname'=>'EduMonitor','lastname'=>'Lectura',
        'email'=>'edumonitor.ws@example.invalid','mnethostid'=>$CFG->mnet_localhost_id]);
    $user = core_user::get_user($id);
}
if ($user->idnumber !== 'EDUMONITOR-WS' || is_siteadmin($user)) { throw new Exception('Cuenta de servicio incompatible'); }
$role = $DB->get_field('role','id',['shortname'=>$servicekey]) ?: create_role('Lectura del aula para EduMonitor', $servicekey, '');
assign_capability('webservice/rest:use', CAP_ALLOW, $role, $sys->id);
role_assign($role,$user->id,$sys->id);
foreach (['moodle/course:view','moodle/course:viewparticipants','moodle/user:viewdetails','moodle/site:viewuseridentity',
    'moodle/user:viewalldetails','moodle/grade:viewall','gradereport/user:view','mod/assign:view','mod/quiz:view'] as $cap) {
    // En todo el sitio (el rol ya está asignado a la cuenta en el contexto del sitio): así la cuenta puede leer también los cursos que se creen después.
    // Todas son capacidades de SOLO LECTURA. Además se mantienen en el curso de Física.
    assign_capability($cap, CAP_ALLOW, $role, $sys->id);
    assign_capability($cap, CAP_ALLOW, $role, $ctx->id);
}
$manual = array_values(array_filter(enrol_get_instances($course->id,true),fn($i)=>$i->enrol==='manual'))[0];
enrol_get_plugin('manual')->enrol_user($manual,$user->id,$role);
if (!$DB->record_exists('external_services_users',['externalserviceid'=>$service->id,'userid'=>$user->id])) {
    $ws->add_ws_authorised_user((object)['externalserviceid'=>$service->id,'userid'=>$user->id]);
}
set_config('enablewebservices', 1);
$protocols = array_filter(explode(',', $CFG->webserviceprotocols ?? ''));
if (!in_array('rest',$protocols,true)) { $protocols[]='rest'; set_config('webserviceprotocols',implode(',',$protocols)); }
$token = $DB->get_field('external_tokens','token',['externalserviceid'=>$service->id,'userid'=>$user->id,'tokentype'=>0]);
echo $token ?: \core_external\util::generate_token(0,$service,$user->id,$sys,0,'','EduMonitor local');
