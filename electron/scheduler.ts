import { fsrs, createEmptyCard, Rating, type Card } from 'ts-fsrs';
import type { ReviewCard } from '../shared/types';
export const weights=[0.212,1.2931,2.3065,8.2956,6.4133,0.8334,3.0194,0.001,1.8722,0.1666,0.796,1.4835,0.0614,0.2629,1.6483,0.6014,1.8729,0.5425,0.0912,0.0658,0.1542];
function serialize(card:Card):ReviewCard {return {...card,due:card.due.toISOString(),last_review:card.last_review?.toISOString()};}
export function newCard(now=new Date()):ReviewCard {return serialize(createEmptyCard(now));}
export function scheduleCard(card:ReviewCard,grade:number,now=new Date(),retention=.9):ReviewCard {
 if(![1,2,3,4].includes(grade))throw new Error('Choose a valid review rating.');
 const scheduler=fsrs({w:weights,request_retention:retention,maximum_interval:36500,enable_fuzz:true,enable_short_term:true,learning_steps:['1m','10m'],relearning_steps:['10m']});
 const input:Card={...card,due:new Date(card.due),last_review:card.last_review?new Date(card.last_review):undefined};
 return serialize(scheduler.next(input,now,grade as Exclude<Rating,Rating.Manual>).card);
}
export function studyDayStart(now:Date,minutes:number):Date {const boundary=new Date(now);boundary.setHours(Math.floor(minutes/60),minutes%60,0,0);if(now<boundary)boundary.setDate(boundary.getDate()-1);return boundary;}
