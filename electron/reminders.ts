export function reminderSlot(now:Date,start:string,end:string,count=10):string|null{
 const minutes=(time:string)=>{const [h,m]=time.split(':').map(Number);return h*60+m;};
 const from=minutes(start),to=minutes(end),current=now.getHours()*60+now.getMinutes();
 if(from===to||!Number.isInteger(count)||count<1||count>100)return null;
 const overnight=from>to;
 if(overnight?(current<from&&current>=to):(current<from||current>=to))return null;
 const day=new Date(now);if(overnight&&current<to)day.setDate(day.getDate()-1);
 const elapsed=(current-from+1440)%1440;
 const duration=(to-from+1440)%1440;
 return `${day.getFullYear()}-${day.getMonth()+1}-${day.getDate()}:${start}:${end}:${count}:${Math.floor(elapsed*count/duration)}`;
}
