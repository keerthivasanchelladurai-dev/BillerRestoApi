#!/usr/bin/env bash
# Black-box authentication/authorization smoke tests for BillerRestoApi.
# Run ONLY against a dedicated test database (it creates a category, an item and an image).
#
#   BASE=http://localhost:5199 KEYFILE=<path to appSettings.secrets.config> \
#   ADMIN_USER=t_admin ADMIN_PASS=... USER_USER=t_user USER_PASS=... bash Tests/auth-smoke.sh
#
# KEYFILE is only used to forge an *expired but correctly signed* token for the expiry test.
BASE=${BASE:-http://localhost:5199}
pass=0; fail=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1 (got $2)"; fail=$((fail+1)); }
code() { curl -s -o /tmp/_body -w "%{http_code}" -m 30 "$@"; }
expect() { local name=$1 want=$2; shift 2; local got; got=$(code "$@"); [ "$got" = "$want" ] && ok "$name" || bad "$name" "$got want $want"; }
json() { sed -n "s/.*\"$1\": *\"\([^\"]*\)\".*/\1/p" /tmp/_body | head -1; }
b64u() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }

echo 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==' | base64 -d > _t.png
login() { curl -s -m 30 -X POST -H "Content-Type: application/json" -d "{\"username\":\"$1\",\"password\":\"$2\"}" "$BASE/api/auth/login"; }
AT=$(login "$ADMIN_USER" "$ADMIN_PASS" | sed -n 's/.*"Token": *"\([^"]*\)".*/\1/p')
UT=$(login "$USER_USER" "$USER_PASS" | sed -n 's/.*"Token": *"\([^"]*\)".*/\1/p')
[ -n "$AT" ] && ok "1 valid admin login issues token" || bad "1 valid admin login issues token" none
[ -n "$UT" ] && ok "1 valid user login issues token" || bad "1 valid user login issues token" none

R=$(login "$ADMIN_USER" "wrong-password")
echo "$R" | grep -q '"Token": *"[^"]' && bad "2 invalid login must not issue token" leaked || ok "2 invalid login issues no token"

expect "3 missing token -> 401" 401 "$BASE/api/categories"
expect "4 malformed token -> 401" 401 -H "Authorization: Bearer not.a.token" "$BASE/api/categories"
expect "4 garbage scheme -> 401" 401 -H "Authorization: Basic abc" "$BASE/api/categories"

# Forge tokens with the configured key (expired) and with a wrong key / alg none (tampered)
KEY=$(sed -n 's/.*Auth:SigningKey" value="\([^"]*\)".*/\1/p' "$KEYFILE")
mk() { # $1=exp offset seconds  $2=key  $3=role
  local now exp h p s
  now=$(date +%s); exp=$((now+$1))
  h=$(printf '{"alg":"HS256","typ":"JWT"}' | b64u)
  p=$(printf '{"iss":"BillerRestoApi","aud":"ThermalPrinterTest","sub":"15","name":"t_admin","role":"%s","iat":%s,"exp":%s}' "$3" "$now" "$exp" | b64u)
  s=$(printf '%s' "$h.$p" | openssl dgst -sha256 -hmac "$2" -binary | b64u)
  echo "$h.$p.$s"
}
expect "5 expired (correctly signed) token -> 401" 401 -H "Authorization: Bearer $(mk -3600 "$KEY" Admin)" "$BASE/api/categories"
expect "forged-valid control token -> 200" 200 -H "Authorization: Bearer $(mk 3600 "$KEY" Admin)" "$BASE/api/categories"
expect "6 token signed with wrong key -> 401" 401 -H "Authorization: Bearer $(mk 3600 "wrong-key-wrong-key-wrong-key-wrong-key" Admin)" "$BASE/api/categories"
H=${UT%%.*}; REST=${UT#*.}; P=${REST%%.*}; S=${REST#*.}
EVIL=$(printf '{"iss":"BillerRestoApi","aud":"ThermalPrinterTest","sub":"15","name":"t_admin","role":"Admin","iat":1,"exp":9999999999}' | b64u)
expect "6 tampered payload (User->Admin), old signature -> 401" 401 -H "Authorization: Bearer $H.$EVIL.$S" "$BASE/api/categories"
NONE=$(printf '{"alg":"none","typ":"JWT"}' | b64u)
expect "6 alg=none token -> 401" 401 -H "Authorization: Bearer $NONE.$EVIL." "$BASE/api/categories"

expect "7 admin token reads categories" 200 -H "Authorization: Bearer $AT" "$BASE/api/categories"
expect "7 user token reads items" 200 -H "Authorization: Bearer $UT" "$BASE/api/items?locationId=1&pageSize=2"
expect "7 user token reads locations" 200 -H "Authorization: Bearer $UT" "$BASE/api/locations"
expect "7 user token reads tables" 200 -H "Authorization: Bearer $UT" "$BASE/api/tables"
expect "7 user token reads bills" 200 -H "Authorization: Bearer $UT" "$BASE/api/bills?pageSize=1"

JSONH="Content-Type: application/json"
expect "8 user cannot create category -> 403" 403 -X POST -H "$JSONH" -H "Authorization: Bearer $UT" -d '{"name":"authtest-forbidden"}' "$BASE/api/categories"
expect "8 user cannot create item -> 403" 403 -X POST -H "$JSONH" -H "Authorization: Bearer $UT" -d '{"name":"x","category_id":1,"price":1}' "$BASE/api/items"
expect "8 user cannot update item -> 403" 403 -X PUT -H "$JSONH" -H "Authorization: Bearer $UT" -d '{"name":"x","category_id":1}' "$BASE/api/items/1"
expect "8 user cannot upload image -> 403" 403 -X POST -H "Authorization: Bearer $UT" -F "file=@_t.png;type=image/png" "$BASE/api/items/1/image"
expect "8 user cannot delete image -> 403" 403 -X DELETE -H "Authorization: Bearer $UT" "$BASE/api/items/1/image"
expect "8 no token cannot create category -> 401" 401 -X POST -H "$JSONH" -d '{"name":"authtest-anon"}' "$BASE/api/categories"

# 10. Login throttling (separate username so the real accounts are not locked)
for i in 1 2 3 4 5; do login "throttle_probe" "bad$i" >/dev/null; done
login "throttle_probe" "bad6" | grep -q "Too many failed attempts" && ok "10 login throttle locks after 5 failures" || bad "10 login throttle" none

# 11. Authorized item/category operations (admin) + creator identity comes from token
CN="authtest-$RANDOM"
expect "11 admin creates category -> 201" 201 -X POST -H "$JSONH" -H "Authorization: Bearer $AT" -d "{\"name\":\"$CN\",\"created_by\":\"spoofed\"}" "$BASE/api/categories"
CID=$(sed -n 's/.*"id": *\([0-9]*\).*/\1/p' /tmp/_body | head -1)
expect "11 admin creates item -> 201" 201 -X POST -H "$JSONH" -H "Authorization: Bearer $AT" -d "{\"name\":\"$CN-item\",\"category_id\":$CID,\"price\":12.5}" "$BASE/api/items"
IID=$(sed -n 's/.*"Id": *\([0-9]*\).*/\1/p;s/.*"id": *\([0-9]*\).*/\1/p' /tmp/_body | head -1)
expect "11 admin updates item -> 200" 200 -X PUT -H "$JSONH" -H "Authorization: Bearer $AT" -d "{\"name\":\"$CN-item2\",\"category_id\":$CID,\"is_kot\":1,\"show_order\":0}" "$BASE/api/items/$IID"

# 12. Billing uses server-side pricing; created_by comes from the token
expect "12 user saves bill -> 201" 201 -X POST -H "$JSONH" -H "Authorization: Bearer $UT" -d "{\"location_id\":2,\"created_by\":\"spoofed\",\"items\":[{\"item_id\":$IID,\"quantity\":2}]}" "$BASE/api/bills"
grep -q '"net_total": *25' /tmp/_body && ok "12 bill net_total = 2 x 12.50 computed by server" || bad "12 bill total" "$(grep net_total /tmp/_body)"
grep -q '"created_by": *"t_user"' /tmp/_body && ok "12 bill created_by is the authenticated user (spoof ignored)" || bad "12 created_by" "$(grep created_by /tmp/_body)"

# 13. Image upload / retrieval (admin)
expect "13 admin uploads image -> 200" 200 -X POST -H "Authorization: Bearer $AT" -F "file=@_t.png;type=image/png" "$BASE/api/items/$IID/image"
IMG=$(json ImagePath)
expect "13 uploaded image is retrievable" 200 "$BASE$IMG"
expect "13 admin deletes image -> 200" 200 -X DELETE -H "Authorization: Bearer $AT" "$BASE/api/items/$IID/image"

expect "health stays public -> 200" 200 "$BASE/api/health/database"
expect "login stays public (bad body -> 400, not 401)" 400 -X POST -H "$JSONH" -d '{}' "$BASE/api/auth/login"

echo; echo "passed=$pass failed=$fail"; [ "$fail" = 0 ]
rm -f _t.png
